// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Infrastructure.Platform;
using Ryx.Sidekick.Editor.Providers;
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Controls when AssetDatabase.Refresh() is invoked after assistant edits.
    /// </summary>
    internal enum AssetRefreshMode
    {
        Off,
        AfterAssistantCompletes,
        AfterEditAndWriteTools,
        Manual
    }

    /// <summary>
    /// Stores settings for the Sidekick Unity integration.
    /// Settings are persisted via EditorPrefs.
    /// </summary>
    [FilePath("UserSettings/Sidekick/Settings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal class SidekickSettings : ScriptableSingleton<SidekickSettings>
    {
        [Serializable]
        internal class CliPathOverrideEntry
        {
            public string providerId;
            public string platformKey;
            public string path;
        }

        [Serializable]
        internal class ProviderUiStateEntry
        {
            public string providerId;
            public string model;
            public string reasoningEffort;
            public string collaborationMode;
            public string permissionMode;
            public string lastOpenedSessionId;
            public bool enableThinking;
            public int maxThinkingTokens = 16000;
            public bool thinkingMigrated;
        }

        /// <summary>
        /// A provider-scoped setting value, persisted in the single settings asset. The container is
        /// owned by the Lite package; the keys/semantics are owned by each provider's settings page
        /// (Claude defines its keys in Lite, Codex/Cursor define theirs in the Pro package). This is how
        /// provider-specific options (e.g. Claude's Bedrock toggle) persist without generic snapshot fields.
        /// </summary>
        [Serializable]
        internal class ProviderSettingEntry
        {
            public string providerId;
            public string key;
            public string value;
        }

        /// <summary>
        /// A single key/value pair for MCP HTTP headers or stdio environment variables.
        /// </summary>
        [Serializable]
        internal class McpKeyValueEntry
        {
            public string key;
            public string value;
        }

        /// <summary>
        /// A single MCP server definition. Serialized to the <c>mcpServers</c> JSON map consumed by
        /// both the Claude <c>--mcp-config</c> file and the Codex app-server config overrides.
        /// </summary>
        [Serializable]
        internal class McpServerEntry
        {
            public string id;
            public string name;
            public bool enabled = true;
            public string transport = "http";
            // retained only for v1→v2 migration; never set true anymore
            public bool isBuiltInUnity;

            // http transport
            public string url;
            public List<McpKeyValueEntry> headers = new List<McpKeyValueEntry>();

            // stdio transport
            public string command;
            public List<string> args = new List<string>();
            public List<McpKeyValueEntry> env = new List<McpKeyValueEntry>();
        }

        [SerializeField] private string providerId = "claude";
        [SerializeField] internal string cliPath = "claude";
        [SerializeField] internal List<CliPathOverrideEntry> cliPathOverrides = new List<CliPathOverrideEntry>();
        [SerializeField] internal List<ProviderUiStateEntry> providerUiStates = new List<ProviderUiStateEntry>();
        [SerializeField] internal List<ProviderModelCatalog> modelCatalogs = new List<ProviderModelCatalog>();
        [SerializeField] internal List<McpServerEntry> mcpServers = new List<McpServerEntry>();
        [SerializeField] internal int mcpSchemaVersion = 0;
        private const int CurrentMcpSchemaVersion = 2;
        [SerializeField] internal List<ProviderSettingEntry> providerSettings = new List<ProviderSettingEntry>();
        [SerializeField] private string workingDirectory = "";
        [SerializeField] private string model = SidekickAppConstants.Models.Opus;
        [SerializeField] private string reasoningEffort = "";
        [SerializeField] private bool verboseLogging;
        [SerializeField] private bool useBedrock;
        [SerializeField] private bool debugMode;
        [SerializeField] private int maxTurns = 50;
        [SerializeField] private string collaborationMode = SidekickAppConstants.CollaborationModes.Default;
        [SerializeField] private string permissionMode = SidekickAppConstants.PermissionModes.Default;
        [SerializeField] private AssetRefreshMode assetRefreshMode = AssetRefreshMode.AfterAssistantCompletes;
        [SerializeField] private bool enableMcpConfig = true;
        [SerializeField] private bool useCustomMcpConfig;
        [SerializeField] private string mcpConfigPath = "";
        [SerializeField] private string mcpPermissionPromptTool = "";
        [SerializeField] private string generatedMcpConfigPath = "Sidekick/mcp-config.generated.json";
        [SerializeField] private string mcpServerUrl = "http://localhost:8080/mcp";
        [SerializeField] private bool autoConnectMcpForUnity = true;
        [SerializeField] private bool autoStartMcpForUnityServer = false;
        [SerializeField] private bool enableThinking;
        [SerializeField] private int maxThinkingTokens = 16000;
        [SerializeField] private string lastOpenedSessionId = "";
        [SerializeField] private string entitlementToken;

        // Sidekick account (ryx-sidekick.pro) session fields
        [SerializeField] private string accountEmail;
        [SerializeField] private string accountPlan;
        [SerializeField] private long accountExpiresAt;
        [SerializeField] private bool accountSignedIn;

        private event Action<ActiveProviderStateSnapshot> _activeProviderStateChanged;
        private ActiveProviderStateSnapshot _lastPublishedActiveProviderState;

        internal event Action<ActiveProviderStateSnapshot> ActiveProviderStateChanged
        {
            add
            {
                var _ = CurrentActiveProviderState;
                _activeProviderStateChanged += value;
            }
            remove => _activeProviderStateChanged -= value;
        }

        internal ActiveProviderStateSnapshot CurrentActiveProviderState
        {
            get
            {
                EnsureActiveProviderUiState();
                NormalizeModeSelection();

                var snapshot = CreateActiveProviderStateSnapshot();
                _lastPublishedActiveProviderState ??= snapshot;
                return snapshot;
            }
        }

        public string ProviderId
        {
            get => providerId;
            set
            {
                PersistActiveProviderUiState();
                MigrateLegacyCliPathIfNeeded(providerId);
                var provider = CliProviderRegistry.GetProvider(value);
                providerId = provider.Id;
                LoadActiveProviderUiState(provider);
                NormalizeModeSelection(provider);
                SaveSettings();
            }
        }

        public ICliProvider ActiveProvider => CliProviderRegistry.GetProvider(providerId);

        public string CliPath
        {
            get => GetCliPath(providerId);
            set => SetCliPath(providerId, value);
        }

        /// <summary>
        /// Resolves the CLI path for a specific provider on the current platform:
        /// platform-specific override -> legacy flat field -> provider default.
        /// </summary>
        internal string GetCliPath(string targetProviderId)
        {
            var provider = CliProviderRegistry.GetProvider(targetProviderId);
            if (provider == null)
            {
                return string.Empty;
            }

            var path = GetCliPathOverride(provider.Id, GetCurrentPlatformKey());
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            return !string.IsNullOrWhiteSpace(cliPath)
                ? cliPath
                : GetDefaultCliPath(provider);
        }

        /// <summary>
        /// Stores the CLI path override for a specific provider on the current platform.
        /// </summary>
        internal void SetCliPath(string targetProviderId, string value)
        {
            MigrateLegacyCliPathIfNeeded(targetProviderId);
            SetCliPathOverride(targetProviderId, GetCurrentPlatformKey(), value);
            SaveSettings();
        }

        public string EntitlementToken
        {
            get => entitlementToken;
            set { entitlementToken = value; SaveSettings(); }
        }

        // Sidekick account session properties
        public string AccountEmail
        {
            get => accountEmail;
            set { accountEmail = value; SaveSettings(); }
        }

        public string AccountPlan
        {
            get => accountPlan;
            set { accountPlan = value; SaveSettings(); }
        }

        public long AccountExpiresAt
        {
            get => accountExpiresAt;
            set { accountExpiresAt = value; SaveSettings(); }
        }

        public bool AccountSignedIn
        {
            get => accountSignedIn;
            set { accountSignedIn = value; SaveSettings(); }
        }

        public string WorkingDirectory
        {
            // Fall back to the project root when unset OR when the stored path no longer
            // exists. A missing cwd is silently fatal under Mono: Process.Start does not
            // throw (unlike CoreCLR) but spawns a process that fails chdir and exits 255
            // with empty stderr, which surfaces as a cryptic "CLI returned error: ".
            get => (string.IsNullOrEmpty(workingDirectory) || !Directory.Exists(workingDirectory))
                ? GetProjectRoot()
                : workingDirectory;
            set { workingDirectory = value; SaveSettings(); }
        }

        public string Model
        {
            get
            {
                EnsureActiveProviderUiState();
                return model;
            }
            set
            {
                EnsureActiveProviderUiState();
                model = value;
                PersistActiveProviderUiState();
                SaveSettings();
            }
        }

        public string ReasoningEffort
        {
            get
            {
                EnsureActiveProviderUiState();
                return reasoningEffort;
            }
            set
            {
                EnsureActiveProviderUiState();
                reasoningEffort = value ?? string.Empty;
                PersistActiveProviderUiState();
                SaveSettings();
            }
        }

        public bool VerboseLogging
        {
            get => verboseLogging;
            set { verboseLogging = value; SaveSettings(); }
        }

        /// <summary>
        /// Bedrock routing for the active provider (Claude-specific in practice). Stored in the
        /// provider-keyed settings bag; falls back to the legacy flat field for one-time migration.
        /// </summary>
        public bool UseBedrock
        {
            get => GetProviderBool(providerId, ProviderSettingKeys.UseBedrock, useBedrock);
            set => SetProviderSetting(providerId, ProviderSettingKeys.UseBedrock, value);
        }

        /// <summary>
        /// When enabled, runs CLI with UseShellExecute=true so the OS opens a visible console window.
        /// Streaming output will NOT appear in Unity; use for debugging CLI issues directly.
        /// </summary>
        public bool DebugMode
        {
            get => debugMode;
            set { debugMode = value; SaveSettings(); }
        }

        public int MaxTurns
        {
            get => maxTurns;
            set { maxTurns = value; SaveSettings(); }
        }

        public string CollaborationMode
        {
            get
            {
                EnsureActiveProviderUiState();
                NormalizeModeSelection();
                return collaborationMode;
            }
            set => SetModeSelection(value, permissionMode);
        }

        public string PermissionMode
        {
            get
            {
                EnsureActiveProviderUiState();
                NormalizeModeSelection();
                return permissionMode;
            }
            set => SetModeSelection(collaborationMode, value);
        }

        /// <summary>
        /// Configures when Unity's AssetDatabase.Refresh() should run after assistant writes/edits.
        /// </summary>
        public AssetRefreshMode AssetRefreshMode
        {
            get => assetRefreshMode;
            set { assetRefreshMode = value; SaveSettings(); }
        }

        /// <summary>
        /// When true, use an explicit MCP config file path instead of generating a temp one.
        /// </summary>
        public bool EnableMcpConfig
        {
            get => enableMcpConfig;
            set { enableMcpConfig = value; SaveSettings(); }
        }

        /// <summary>
        /// When true, use an explicit MCP config file path instead of generating a temp one.
        /// </summary>
        public bool UseCustomMcpConfig
        {
            get => useCustomMcpConfig;
            set { useCustomMcpConfig = value; SaveSettings(); }
        }

        /// <summary>
        /// Path to a user-provided MCP config JSON.
        /// </summary>
        public string McpConfigPath
        {
            get => mcpConfigPath;
            set { mcpConfigPath = value; SaveSettings(); }
        }

        public string McpPermissionPromptTool
        {
            get => mcpPermissionPromptTool;
            set { mcpPermissionPromptTool = value; SaveSettings(); }
        }

        /// <summary>
        /// Destination path for the generated MCP config (relative to project root or absolute).
        /// </summary>
        public string GeneratedMcpConfigPath
        {
            get => generatedMcpConfigPath;
            set { generatedMcpConfigPath = value; SaveSettings(); }
        }

        /// <summary>
        /// URL for the MCP for Unity HTTP server endpoint.
        /// Used only by the onboarding MCP probe (<c>OnboardingWizardPresenter</c>); not consumed by the
        /// settings UI since the built-in entry was removed in schema v2 (B1).
        /// </summary>
        public string McpServerUrl
        {
            get => mcpServerUrl;
            set { mcpServerUrl = value; SaveSettings(); }
        }

        /// <summary>
        /// When enabled, automatically attempts to connect to an already running MCP for Unity server on window open.
        /// </summary>
        public bool AutoConnectMcpForUnity
        {
            get => autoConnectMcpForUnity;
            set { autoConnectMcpForUnity = value; SaveSettings(); }
        }

        /// <summary>
        /// When enabled, automatically starts the MCP for Unity server if not already running on window open.
        /// </summary>
        public bool AutoStartMcpForUnityServer
        {
            get => autoStartMcpForUnityServer;
            set { autoStartMcpForUnityServer = value; SaveSettings(); }
        }

        /// <summary>
        /// When enabled, adds --max-thinking-tokens to CLI args, enabling extended thinking mode.
        /// </summary>
        public bool EnableThinking
        {
            get
            {
                EnsureActiveProviderUiState();
                return enableThinking;
            }
            set
            {
                EnsureActiveProviderUiState();
                enableThinking = value;
                SaveSettings();
            }
        }

        /// <summary>
        /// Number of tokens to budget for extended thinking (used when EnableThinking is true).
        /// </summary>
        public int MaxThinkingTokens
        {
            get
            {
                EnsureActiveProviderUiState();
                return maxThinkingTokens;
            }
            set
            {
                EnsureActiveProviderUiState();
                maxThinkingTokens = value;
                SaveSettings();
            }
        }

        /// <summary>
        /// The session ID of the last opened conversation in this project.
        /// Used to restore the same chat on window reopen, even if newer chats exist.
        /// </summary>
        public string LastOpenedSessionId
        {
            get
            {
                EnsureActiveProviderUiState();
                return lastOpenedSessionId;
            }
            set
            {
                EnsureActiveProviderUiState();
                lastOpenedSessionId = value;
                PersistActiveProviderUiState();
                SaveSettings();
            }
        }

        /// <summary>
        /// Saves settings to disk.
        /// </summary>
        public void SaveSettings()
        {
            PersistActiveProviderUiState();
            MigrateLegacyCliPathIfNeeded(providerId);
            Save(true);
            PublishActiveProviderStateChangedIfNeeded();
        }

        public void NormalizeModeSelection()
        {
            NormalizeModeSelection(ActiveProvider);
        }

        private void NormalizeModeSelection(ICliProvider provider)
        {
            EnsureActiveProviderUiState(provider);
            provider ??= ActiveProvider;
            if (provider == null)
            {
                return;
            }

            var desiredCollaborationMode = string.IsNullOrWhiteSpace(collaborationMode)
                ? SidekickAppConstants.CollaborationModes.Default
                : collaborationMode;
            var desiredPermissionMode = permissionMode;

            if (desiredPermissionMode == SidekickAppConstants.PermissionModes.Plan)
            {
                desiredCollaborationMode = SidekickAppConstants.CollaborationModes.Plan;
                desiredPermissionMode = provider.GetPermissionModes(desiredCollaborationMode).FirstOrDefault().Value
                    ?? SidekickAppConstants.PermissionModes.Default;
            }

            var normalized = provider.NormalizeModeSelection(desiredCollaborationMode, desiredPermissionMode);
            if (normalized.CollaborationMode == collaborationMode && normalized.PermissionMode == permissionMode)
            {
                return;
            }

            collaborationMode = normalized.CollaborationMode;
            permissionMode = normalized.PermissionMode;
            PersistActiveProviderUiState();
            SaveSettings();
        }

        private void SetModeSelection(string desiredCollaborationMode, string desiredPermissionMode)
        {
            EnsureActiveProviderUiState();
            if (desiredPermissionMode == SidekickAppConstants.PermissionModes.Plan)
            {
                desiredCollaborationMode = SidekickAppConstants.CollaborationModes.Plan;
                desiredPermissionMode = ActiveProvider.GetPermissionModes(desiredCollaborationMode).FirstOrDefault().Value
                    ?? SidekickAppConstants.PermissionModes.Default;
            }

            var normalized = ActiveProvider.NormalizeModeSelection(desiredCollaborationMode, desiredPermissionMode);
            collaborationMode = normalized.CollaborationMode;
            permissionMode = normalized.PermissionMode;
            PersistActiveProviderUiState();
            SaveSettings();
        }

        internal ProviderUiStateSnapshot GetProviderUiState(string targetProviderId)
        {
            var entry = GetOrCreateProviderUiStateEntry(targetProviderId, createIfMissing: false);
            var provider = CliProviderRegistry.GetProvider(targetProviderId);
            return new ProviderUiStateSnapshot
            {
                // Raw id, not provider?.Id: the registry falls back to claude for
                // unknown ids and the snapshot must not be re-labelled as claude's.
                ProviderId = targetProviderId,
                SelectedSessionId = entry?.lastOpenedSessionId ?? string.Empty,
                Model = entry?.model ?? provider?.DefaultModel ?? model,
                ReasoningEffort = entry?.reasoningEffort ?? string.Empty,
                CollaborationMode = entry?.collaborationMode ?? SidekickAppConstants.CollaborationModes.Default,
                PermissionMode = entry?.permissionMode
                                 ?? provider?.GetPermissionModes(entry?.collaborationMode ?? SidekickAppConstants.CollaborationModes.Default).FirstOrDefault().Value
                                 ?? SidekickAppConstants.PermissionModes.Default,
                EnableThinking = entry?.enableThinking ?? enableThinking,
                MaxThinkingTokens = entry != null && entry.maxThinkingTokens > 0 ? entry.maxThinkingTokens : maxThinkingTokens
            };
        }

        internal void SaveProviderUiState(ProviderUiStateSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ProviderId))
            {
                return;
            }

            // Key the entry by the snapshot's raw id: the registry falls back to claude
            // for unknown ids, and writing under the fallback's id would clobber
            // claude's entry with another provider's state.
            var provider = CliProviderRegistry.GetProvider(snapshot.ProviderId);
            var entry = GetOrCreateProviderUiStateEntry(snapshot.ProviderId, createIfMissing: true);
            entry.model = !string.IsNullOrWhiteSpace(snapshot.Model)
                ? snapshot.Model
                : provider.DefaultModel;
            entry.reasoningEffort = snapshot.ReasoningEffort ?? string.Empty;

            var normalized = provider.NormalizeModeSelection(
                string.IsNullOrWhiteSpace(snapshot.CollaborationMode)
                    ? SidekickAppConstants.CollaborationModes.Default
                    : snapshot.CollaborationMode,
                string.IsNullOrWhiteSpace(snapshot.PermissionMode)
                    ? provider.GetPermissionModes(snapshot.CollaborationMode ?? SidekickAppConstants.CollaborationModes.Default).FirstOrDefault().Value
                    : snapshot.PermissionMode);

            entry.collaborationMode = normalized.CollaborationMode;
            entry.permissionMode = normalized.PermissionMode;
            entry.lastOpenedSessionId = snapshot.SelectedSessionId ?? string.Empty;
            entry.enableThinking = snapshot.EnableThinking;
            entry.maxThinkingTokens = snapshot.MaxThinkingTokens > 0 ? snapshot.MaxThinkingTokens : 16000;
            entry.thinkingMigrated = true;

            if (string.Equals(providerId, snapshot.ProviderId, StringComparison.Ordinal))
            {
                model = entry.model;
                reasoningEffort = entry.reasoningEffort;
                collaborationMode = entry.collaborationMode;
                permissionMode = entry.permissionMode;
                lastOpenedSessionId = entry.lastOpenedSessionId;
                enableThinking = entry.enableThinking;
                maxThinkingTokens = entry.maxThinkingTokens;
            }

            SaveSettings();
        }

        internal ProviderModelCatalog GetModelCatalog(string targetProviderId)
        {
            if (string.IsNullOrWhiteSpace(targetProviderId))
            {
                return null;
            }

            modelCatalogs ??= new List<ProviderModelCatalog>();
            return modelCatalogs.FirstOrDefault(catalog =>
                string.Equals(catalog.ProviderId, targetProviderId, StringComparison.Ordinal));
        }

        internal void SaveModelCatalog(ProviderModelCatalog catalog)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(catalog.ProviderId))
            {
                return;
            }

            modelCatalogs ??= new List<ProviderModelCatalog>();
            var index = modelCatalogs.FindIndex(existing =>
                string.Equals(existing.ProviderId, catalog.ProviderId, StringComparison.Ordinal));
            if (index >= 0)
            {
                modelCatalogs[index] = catalog;
            }
            else
            {
                modelCatalogs.Add(catalog);
            }

            SaveSettings();
        }

        private void EnsureActiveProviderUiState()
        {
            EnsureActiveProviderUiState(ActiveProvider);
        }

        private void EnsureActiveProviderUiState(ICliProvider provider)
        {
            provider ??= ActiveProvider;
            if (provider == null)
            {
                return;
            }

            LoadActiveProviderUiState(provider);
        }

        private void LoadActiveProviderUiState(ICliProvider provider)
        {
            provider ??= ActiveProvider;
            if (provider == null)
            {
                return;
            }

            var entry = GetOrCreateProviderUiStateEntry(provider.Id, createIfMissing: false);
            entry ??= GetOrCreateProviderUiStateEntry(provider.Id, createIfMissing: true);

            // One-time seeding of per-provider thinking from the legacy global flat fields.
            // Guarded per-entry so we never clobber a previously-configured per-provider value.
            if (!entry.thinkingMigrated)
            {
                entry.enableThinking = enableThinking;
                entry.maxThinkingTokens = maxThinkingTokens > 0 ? maxThinkingTokens : 16000;
                entry.thinkingMigrated = true;
            }

            model = !string.IsNullOrWhiteSpace(entry.model)
                ? entry.model
                : provider.DefaultModel;
            reasoningEffort = entry.reasoningEffort ?? string.Empty;

            var desiredCollaborationMode = !string.IsNullOrWhiteSpace(entry.collaborationMode)
                ? entry.collaborationMode
                : SidekickAppConstants.CollaborationModes.Default;

            var desiredPermissionMode = !string.IsNullOrWhiteSpace(entry.permissionMode)
                ? entry.permissionMode
                : provider.GetPermissionModes(desiredCollaborationMode).FirstOrDefault().Value;

            var normalized = provider.NormalizeModeSelection(desiredCollaborationMode, desiredPermissionMode);
            collaborationMode = normalized.CollaborationMode;
            permissionMode = normalized.PermissionMode;
            lastOpenedSessionId = entry.lastOpenedSessionId ?? string.Empty;
            enableThinking = entry.enableThinking;
            maxThinkingTokens = entry.maxThinkingTokens > 0 ? entry.maxThinkingTokens : 16000;

            entry.model = model;
            entry.reasoningEffort = reasoningEffort;
            entry.collaborationMode = collaborationMode;
            entry.permissionMode = permissionMode;
            entry.lastOpenedSessionId = lastOpenedSessionId;
        }

        private void PersistActiveProviderUiState()
        {
            // Persist strictly under the raw stored id: the flat fields belong to that
            // provider even when the registry no longer knows it. Resolving through
            // ActiveProvider here would silently fall back to claude and clobber its
            // entry with another provider's state (e.g. a test fake's "auto" model).
            var entry = GetOrCreateProviderUiStateEntry(providerId, createIfMissing: true);
            if (entry == null)
            {
                return;
            }

            entry.model = model;
            entry.reasoningEffort = reasoningEffort;
            entry.collaborationMode = collaborationMode;
            entry.permissionMode = permissionMode;
            entry.lastOpenedSessionId = lastOpenedSessionId;
            entry.enableThinking = enableThinking;
            entry.maxThinkingTokens = maxThinkingTokens > 0 ? maxThinkingTokens : 16000;
            entry.thinkingMigrated = true;
        }

        private ActiveProviderStateSnapshot CreateActiveProviderStateSnapshot()
        {
            return new ActiveProviderStateSnapshot(providerId, model, collaborationMode, permissionMode, reasoningEffort);
        }

        private void PublishActiveProviderStateChangedIfNeeded()
        {
            var snapshot = CreateActiveProviderStateSnapshot();
            if (_lastPublishedActiveProviderState == null)
            {
                _lastPublishedActiveProviderState = snapshot;
                return;
            }

            if (_lastPublishedActiveProviderState.Equals(snapshot))
            {
                return;
            }

            _lastPublishedActiveProviderState = snapshot;
            _activeProviderStateChanged?.Invoke(snapshot);
        }

        private ProviderUiStateEntry GetOrCreateProviderUiStateEntry(string targetProviderId, bool createIfMissing)
        {
            if (string.IsNullOrWhiteSpace(targetProviderId))
            {
                return null;
            }

            providerUiStates ??= new List<ProviderUiStateEntry>();
            var entry = providerUiStates.FirstOrDefault(state => string.Equals(state.providerId, targetProviderId, StringComparison.Ordinal));
            if (entry != null || !createIfMissing)
            {
                return entry;
            }

            entry = new ProviderUiStateEntry
            {
                providerId = targetProviderId,
                model = null,
                collaborationMode = null,
                permissionMode = null,
                lastOpenedSessionId = string.Empty
            };
            providerUiStates.Add(entry);
            return entry;
        }

        /// <summary>
        /// Removes the persisted UI state entry for a provider. Test hygiene API: fake
        /// providers registered by EditMode tests must clean up the entries they create
        /// in the real UserSettings asset.
        /// </summary>
        internal void RemoveProviderUiState(string targetProviderId)
        {
            if (string.IsNullOrWhiteSpace(targetProviderId) || providerUiStates == null)
            {
                return;
            }

            var removed = providerUiStates.RemoveAll(
                state => string.Equals(state.providerId, targetProviderId, StringComparison.Ordinal));
            if (removed > 0)
            {
                Save(true);
            }
        }

        private static string GetProjectRoot()
        {
            return Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        }

        /// <summary>
        /// Resolves the CLI path using platform-specific logic.
        /// </summary>
        public string GetResolvedCliPath()
        {
            var platform = ClaudePlatformFactory.GetPlatform();
            return platform.ResolveCliPath(CliPath, ActiveProvider.GetDefaultCliPaths());
        }

        /// <summary>
        /// Creates a ProcessStartInfo configured for the current provider and platform.
        /// </summary>
        public ProcessStartInfo CreateProcessStartInfo(string arguments)
        {
            return ActiveProvider.CreateProcessStartInfo(CliPath, arguments, WorkingDirectory, debugMode, UseBedrock);
        }

        /// <summary>
        /// Validates that the CLI is reachable and returns version info.
        /// </summary>
        public (bool success, string message) ValidateCli()
        {
            return ActiveProvider.ValidateCli(CliPath, WorkingDirectory);
        }

        /// <summary>
        /// Builds the argument string for the active CLI provider.
        /// </summary>
        public string BuildArguments(
            string prompt = null,
            bool printMode = true,
            bool continueSession = false,
            string sessionId = null,
            PromptTransportMode promptTransportMode = PromptTransportMode.Argument,
            bool includePrompt = true,
            IReadOnlyList<string> imageAttachmentPaths = null)
        {
            var ctx = new CliArgumentContext
            {
                Prompt = prompt,
                PrintMode = printMode,
                ContinueSession = continueSession,
                SessionId = sessionId,
                PromptTransportMode = promptTransportMode,
                IncludePrompt = includePrompt,
                Model = model,
                ReasoningEffort = reasoningEffort,
                CollaborationMode = CollaborationMode,
                PermissionMode = PermissionMode,
                MaxTurns = maxTurns,
                EnableThinking = enableThinking,
                MaxThinkingTokens = maxThinkingTokens,
                WorkingDirectory = WorkingDirectory,
                ImageAttachmentPaths = imageAttachmentPaths,
            };
            return ActiveProvider.BuildArguments(ctx);
        }

        private static string GetCurrentPlatformKey()
        {
            return Application.platform switch
            {
                RuntimePlatform.OSXEditor => "macos",
                RuntimePlatform.WindowsEditor => "windows",
                _ => "linux"
            };
        }

        private string GetCliPathOverride(string targetProviderId, string platformKey)
        {
            EnsureCliPathOverridesInitialized();
            return cliPathOverrides
                .FirstOrDefault(entry =>
                    string.Equals(entry.providerId, targetProviderId, StringComparison.Ordinal) &&
                    string.Equals(entry.platformKey, platformKey, StringComparison.Ordinal))
                ?.path;
        }

        private void SetCliPathOverride(string targetProviderId, string platformKey, string path)
        {
            EnsureCliPathOverridesInitialized();
            var existingEntry = cliPathOverrides.FirstOrDefault(entry =>
                string.Equals(entry.providerId, targetProviderId, StringComparison.Ordinal) &&
                string.Equals(entry.platformKey, platformKey, StringComparison.Ordinal));

            if (string.IsNullOrWhiteSpace(path))
            {
                if (existingEntry != null)
                {
                    cliPathOverrides.Remove(existingEntry);
                }

                return;
            }

            if (existingEntry == null)
            {
                cliPathOverrides.Add(new CliPathOverrideEntry
                {
                    providerId = targetProviderId,
                    platformKey = platformKey,
                    path = path
                });
                return;
            }

            existingEntry.path = path;
        }

        private string GetDefaultCliPath(ICliProvider provider)
        {
            provider ??= ActiveProvider;
            if (provider == null)
            {
                return string.Empty;
            }

            return provider.GetDefaultCliPaths().FirstOrDefault(File.Exists) ?? provider.DefaultBinaryName;
        }

        private void MigrateLegacyCliPathIfNeeded(string ownerProviderId)
        {
            if (string.IsNullOrWhiteSpace(ownerProviderId) || string.IsNullOrWhiteSpace(cliPath))
            {
                return;
            }

            var platformKey = GetCurrentPlatformKey();
            if (string.IsNullOrWhiteSpace(GetCliPathOverride(ownerProviderId, platformKey)))
            {
                SetCliPathOverride(ownerProviderId, platformKey, cliPath);
            }

            cliPath = string.Empty;
        }

        private void EnsureCliPathOverridesInitialized()
        {
            cliPathOverrides ??= new List<CliPathOverrideEntry>();
        }

        #region Provider settings bag

        /// <summary>
        /// Returns a provider-scoped setting value (raw string), or <paramref name="defaultValue"/> if unset.
        /// Keys are owned by each provider's settings page; the store is the single settings asset.
        /// </summary>
        internal string GetProviderSetting(string targetProviderId, string key, string defaultValue = null)
        {
            if (string.IsNullOrEmpty(targetProviderId) || string.IsNullOrEmpty(key))
            {
                return defaultValue;
            }

            providerSettings ??= new List<ProviderSettingEntry>();
            var entry = providerSettings.FirstOrDefault(e =>
                string.Equals(e.providerId, targetProviderId, StringComparison.Ordinal) &&
                string.Equals(e.key, key, StringComparison.Ordinal));
            return entry?.value ?? defaultValue;
        }

        internal bool GetProviderBool(string targetProviderId, string key, bool defaultValue = false)
        {
            var raw = GetProviderSetting(targetProviderId, key);
            return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
        }

        internal int GetProviderInt(string targetProviderId, string key, int defaultValue = 0)
        {
            var raw = GetProviderSetting(targetProviderId, key);
            return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
        }

        internal void SetProviderSetting(string targetProviderId, string key, string value)
        {
            if (string.IsNullOrEmpty(targetProviderId) || string.IsNullOrEmpty(key))
            {
                return;
            }

            providerSettings ??= new List<ProviderSettingEntry>();
            var entry = providerSettings.FirstOrDefault(e =>
                string.Equals(e.providerId, targetProviderId, StringComparison.Ordinal) &&
                string.Equals(e.key, key, StringComparison.Ordinal));
            if (entry == null)
            {
                entry = new ProviderSettingEntry { providerId = targetProviderId, key = key };
                providerSettings.Add(entry);
            }

            entry.value = value;
            SaveSettings();
        }

        internal void SetProviderSetting(string targetProviderId, string key, bool value)
            => SetProviderSetting(targetProviderId, key, value ? "true" : "false");

        internal void SetProviderSetting(string targetProviderId, string key, int value)
            => SetProviderSetting(targetProviderId, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        #endregion

        #region MCP servers

        /// <summary>
        /// Returns the configured MCP servers (running schema migrations as needed).
        /// The returned list is the live backing list; mutate via <see cref="AddMcpServer"/>,
        /// <see cref="RemoveMcpServer"/> or edit entries in place then call <see cref="SaveSettings"/>.
        /// </summary>
        internal List<McpServerEntry> GetMcpServers()
        {
            EnsureMcpServersInitialized();
            return mcpServers;
        }

        internal McpServerEntry AddMcpServer()
        {
            EnsureMcpServersInitialized();
            var entry = new McpServerEntry
            {
                id = Guid.NewGuid().ToString("N"),
                name = string.Empty,
                enabled = true,
                transport = "http",
                isBuiltInUnity = false,
                url = string.Empty
            };
            mcpServers.Add(entry);
            SaveSettings();
            return entry;
        }

        internal void RemoveMcpServer(string id)
        {
            EnsureMcpServersInitialized();
            var entry = mcpServers.FirstOrDefault(server =>
                server != null && string.Equals(server.id, id, StringComparison.Ordinal));

            if (entry == null)
            {
                return;
            }

            mcpServers.Remove(entry);
            SaveSettings();
        }

        private void EnsureMcpServersInitialized()
        {
            mcpServers ??= new List<McpServerEntry>();
            MigrateLegacyMcpIfNeeded();
        }

        /// <summary>
        /// MCP schema migration. v2 removes the previously-seeded built-in Unity MCP (Coplay) entry —
        /// it is no longer bundled or special-cased (see Documentation~/McpRework/01-coplay-default-off.md).
        /// Fresh installs start with an empty server list. User-defined entries are never touched. Idempotent.
        /// </summary>
        private void MigrateLegacyMcpIfNeeded()
        {
            if (mcpSchemaVersion >= CurrentMcpSchemaVersion)
            {
                return;
            }

            mcpServers.RemoveAll(entry => entry != null && entry.isBuiltInUnity);
            mcpSchemaVersion = CurrentMcpSchemaVersion;
            SaveSettings();
        }

        #endregion
    }

    /// <summary>
    /// Well-known keys for the provider-scoped settings bag (<see cref="SidekickSettings.GetProviderSetting"/>).
    /// Each provider/package owns its own keys; this holds the Lite/Claude ones.
    /// </summary>
    internal static class ProviderSettingKeys
    {
        public const string UseBedrock = "useBedrock";
    }
}
