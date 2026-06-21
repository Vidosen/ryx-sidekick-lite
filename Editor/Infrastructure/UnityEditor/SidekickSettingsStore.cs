// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor
{
    internal sealed class SidekickSettingsStore : ISettingsStore
    {
        private readonly SidekickSettings _settings;

        public SidekickSettingsStore()
        {
            _settings = SidekickSettings.instance;
        }

        public event Action<ActiveProviderStateSnapshot> ActiveProviderStateChanged
        {
            add => _settings.ActiveProviderStateChanged += value;
            remove => _settings.ActiveProviderStateChanged -= value;
        }

        public string ProviderId
        {
            get => _settings.ProviderId;
            set => _settings.ProviderId = value;
        }

        public string Model
        {
            get => _settings.Model;
            set => _settings.Model = value;
        }

        public string ReasoningEffort
        {
            get => _settings.ReasoningEffort;
            set => _settings.ReasoningEffort = value;
        }

        public string CollaborationMode
        {
            get => _settings.CollaborationMode;
            set => _settings.CollaborationMode = value;
        }

        public string PermissionMode
        {
            get => _settings.PermissionMode;
            set => _settings.PermissionMode = value;
        }

        public string LastOpenedSessionId
        {
            get => _settings.LastOpenedSessionId;
            set => _settings.LastOpenedSessionId = value;
        }

        public string WorkingDirectory => _settings.WorkingDirectory;
        public bool VerboseLogging => _settings.VerboseLogging;
        public bool DebugMode => _settings.DebugMode;

        public bool EnableThinking
        {
            get => _settings.EnableThinking;
            set => _settings.EnableThinking = value;
        }

        public int MaxThinkingTokens => _settings.MaxThinkingTokens;
        public bool AutoConnectMcpForUnity => _settings.AutoConnectMcpForUnity;
        public bool AutoStartMcpForUnityServer => _settings.AutoStartMcpForUnityServer;
        public ActiveProviderStateSnapshot CurrentActiveProviderState => _settings.CurrentActiveProviderState;

        public string GetCliPath()
        {
            return _settings.CliPath;
        }

        public (bool success, string message) ValidateCli()
        {
            return _settings.ValidateCli();
        }

        public ProviderUiStateSnapshot GetProviderUiState(string providerId)
        {
            return _settings.GetProviderUiState(providerId);
        }

        public void SaveProviderUiState(ProviderUiStateSnapshot snapshot)
        {
            _settings.SaveProviderUiState(snapshot);
        }

        public ProviderModelCatalog GetModelCatalog(string providerId)
        {
            return _settings.GetModelCatalog(providerId);
        }

        public void SaveModelCatalog(ProviderModelCatalog catalog)
        {
            _settings.SaveModelCatalog(catalog);
        }

        public string BuildArguments(
            string prompt = null,
            bool printMode = true,
            bool continueSession = false,
            string sessionId = null,
            PromptTransportMode promptTransportMode = PromptTransportMode.Argument,
            bool includePrompt = true,
            IReadOnlyList<string> imageAttachmentPaths = null)
        {
            return _settings.BuildArguments(
                prompt,
                printMode,
                continueSession,
                sessionId,
                promptTransportMode,
                includePrompt,
                imageAttachmentPaths);
        }

        public ProcessStartInfo CreateProcessStartInfo(string arguments)
        {
            return _settings.CreateProcessStartInfo(arguments);
        }
    }
}
