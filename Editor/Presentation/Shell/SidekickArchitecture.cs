// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Infrastructure.Mcp;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Permissions;
using Ryx.Sidekick.Editor.UseCases.Updates;
using Ryx.Sidekick.Editor.Presentation.Presenters;
using Unity.AppUI.MVVM;
using UnityEngine;
using ILogger = Ryx.Sidekick.Editor.UseCases.Contracts.ILogger;

namespace Ryx.Sidekick.Editor
{

    internal interface ISidekickControllerGraphFactory
    {
        SidekickWindowScopeGraph CreateWindowScopeGraph(
            Func<IProviderUiMetadata> getActiveProviderMetadata);

        SidekickProviderScopeGraph CreateProviderScopeGraph(
            string providerId,
            SidekickWindowScopeGraph windowScopeGraph,
            IServiceScope providerServiceScope);
    }

    internal interface ISidekickWindowHost : IDisposable
    {
        string HostToken { get; }
        string CurrentProviderId { get; }
        string CurrentSessionId { get; }
        bool IsTurnActive { get; }

        void Initialize();
        void BindView(WindowViewBindingPresenter viewBindingPresenter);
        void OnFocus();
        bool SwitchProvider(string providerId);
        void AutoResumeAfterDomainReload(string providerId, string sessionId);
        InputFieldState CaptureInputFieldState();
        void RestoreInputFieldState(InputFieldState state);
    }

    internal sealed class SidekickWindowScopeGraph
    {
        public SidekickWindowScopeGraph(
            IAuthService authService,
            AttachmentController attachmentController,
            AssetRefreshController assetRefreshController,
            Presentation.Shell.ProviderMenuDisplayBinder providerMenuDisplayBinder,
            PermissionService permissionService,
            PermissionController permissionController,
            PermissionOverlayController permissionOverlayController,
            AskUserQuestionController askUserQuestionController,
            AuthController authController,
            McpForUnityController mcpController,
            IMarkdownContentRenderer markdownRenderer,
            MarkdownRenderContext markdownContext,
            Presentation.Renderers.IMessageElementFactory messageElementFactory,
            Presentation.ViewModels.ProviderSelectorViewModel providerSelectorViewModel = null,
            Presentation.ViewModels.ChatTimelineViewModel chatTimelineViewModel = null,
            Presentation.ViewModels.PaywallViewModel paywallViewModel = null,
            IProPresence proPresence = null,
            CheckForUpdatesQuery checkForUpdatesQuery = null,
            IRemoteConfigSource remoteConfigSource = null,
            IExternalUrlOpener externalUrlOpener = null,
            IDismissStore dismissStore = null,
            SidekickAccountController accountController = null,
            UseCases.Pro.ResolveProAccessStateQuery resolveProAccessState = null)
        {
            AuthService = authService;
            AttachmentController = attachmentController;
            AssetRefreshController = assetRefreshController;
            ProviderMenuDisplayBinder = providerMenuDisplayBinder;
            PermissionService = permissionService;
            PermissionController = permissionController;
            PermissionOverlayController = permissionOverlayController;
            PermissionOverlayViewModel = permissionOverlayController?.ViewModel;
            AskUserQuestionController = askUserQuestionController;
            AskUserQuestionViewModel = askUserQuestionController?.ViewModel;
            AuthController = authController;
            McpController = mcpController;
            MarkdownRenderer = markdownRenderer;
            MarkdownContext = markdownContext;
            MessageElementFactory = messageElementFactory;
            ProviderSelectorViewModel = providerSelectorViewModel;
            ChatTimelineViewModel = chatTimelineViewModel;
            PaywallViewModel = paywallViewModel;
            ProPresence = proPresence;
            CheckForUpdatesQuery = checkForUpdatesQuery;
            RemoteConfigSource = remoteConfigSource;
            ExternalUrlOpener = externalUrlOpener;
            DismissStore = dismissStore;
            AccountController = accountController;
            ResolveProAccessState = resolveProAccessState;
        }

        public IAuthService AuthService { get; }
        public AttachmentController AttachmentController { get; }
        public AssetRefreshController AssetRefreshController { get; }
        public Presentation.Shell.ProviderMenuDisplayBinder ProviderMenuDisplayBinder { get; }
        public PermissionService PermissionService { get; }
        public PermissionController PermissionController { get; }
        public PermissionOverlayController PermissionOverlayController { get; }
        public Presentation.ViewModels.PermissionOverlayViewModel PermissionOverlayViewModel { get; }
        public AskUserQuestionController AskUserQuestionController { get; }
        public Presentation.ViewModels.AskUserQuestionViewModel AskUserQuestionViewModel { get; }
        public AuthController AuthController { get; }
        public McpForUnityController McpController { get; }
        public SidekickAccountController AccountController { get; }
        public IMarkdownContentRenderer MarkdownRenderer { get; }
        public MarkdownRenderContext MarkdownContext { get; }
        public Presentation.Renderers.IMessageElementFactory MessageElementFactory { get; }
        public Presentation.ViewModels.ProviderSelectorViewModel ProviderSelectorViewModel { get; }
        public Presentation.ViewModels.ChatTimelineViewModel ChatTimelineViewModel { get; }
        public Presentation.ViewModels.PaywallViewModel PaywallViewModel { get; }
        public IProPresence ProPresence { get; }

        // Update-notification services — used by WindowViewBindingPresenter.BindUpdates()
        // to construct UpdateNotifier + UpdateNotificationViewModel once the panel is live.
        public CheckForUpdatesQuery CheckForUpdatesQuery { get; }
        public IRemoteConfigSource RemoteConfigSource { get; }
        public IExternalUrlOpener ExternalUrlOpener { get; }
        public IDismissStore DismissStore { get; }

        // Entitlement-aware gate state (Installed / OwnedNotInstalled / Locked) consumed by
        // WindowViewBindingPresenter for the status-bar chip + auto-show install nudge.
        public UseCases.Pro.ResolveProAccessStateQuery ResolveProAccessState { get; }

        public void Dispose()
        {
            PermissionOverlayViewModel?.Dispose();
            AskUserQuestionViewModel?.Dispose();
            ProviderSelectorViewModel?.Dispose();
            ChatTimelineViewModel?.Dispose();
            PaywallViewModel?.Dispose();
        }
    }

    internal sealed class SidekickProviderScopeGraph
    {
        public SidekickProviderScopeGraph(
            IServiceScope providerServiceScope,
            IProviderScope providerScope,
            ConversationController conversationController,
            ChatController chatController,
            Presentation.ViewModels.ComposerViewModel composerViewModel = null)
        {
            ProviderServiceScope = providerServiceScope;
            ProviderScope = providerScope;
            ConversationController = conversationController;
            ChatController = chatController;
            ComposerViewModel = composerViewModel;
        }

        public IServiceScope ProviderServiceScope { get; }
        public IProviderScope ProviderScope { get; }
        public ConversationController ConversationController { get; }
        public ChatController ChatController { get; }
        public Presentation.ViewModels.ComposerViewModel ComposerViewModel { get; }
    }

    internal sealed class UnitySidekickLogger : ILogger
    {
        private readonly ISettingsStore _settingsStore;

        public UnitySidekickLogger(ISettingsStore settingsStore)
        {
            _settingsStore = settingsStore;
        }

        public void Log(string message)
        {
            Debug.Log(message);
        }

        public void LogWarning(string message)
        {
            Debug.LogWarning(message);
        }

        public void LogError(string message)
        {
            Debug.LogError(message);
        }

        public void LogVerbose(string message)
        {
            if (_settingsStore?.VerboseLogging == true)
            {
                Debug.Log(message);
            }
        }
    }

    internal sealed class ProviderUiMetadataAdapter : IProviderUiMetadata
    {
        private readonly ICliProvider _provider;

        public ProviderUiMetadataAdapter(ICliProvider provider)
        {
            _provider = provider;
        }

        public string Id => _provider.Id;
        public string DisplayName => _provider.DisplayName;
        public string[] ModelPresets => _provider.ModelPresets;
        public ProviderModelCatalog FallbackModelCatalog => ProviderModelCatalogFactory.FromProvider(_provider);
        public string DefaultModel => _provider.DefaultModel;
        public AuthOnboardingKind AuthKind => _provider.GetOnboardingInfo()?.AuthKind ?? AuthOnboardingKind.OAuthBuiltIn;
        public CollaborationModeDescriptor[] CollaborationModes => _provider.CollaborationModes;
        public bool SupportsThinking => _provider.SupportsThinking;

        public PermissionModeDescriptor[] GetPermissionModes(string collaborationMode)
        {
            return _provider.GetPermissionModes(collaborationMode);
        }

        public ProviderModeSelection NormalizeModeSelection(string collaborationMode, string permissionMode)
        {
            return _provider.NormalizeModeSelection(collaborationMode, permissionMode);
        }

        public bool IsAutoApprovePermissionMode(string permissionMode)
        {
            return _provider.IsAutoApprovePermissionMode(permissionMode);
        }
    }

    internal sealed class ProviderConversationRepository : IConversationRepository
    {
        private readonly ICliHistoryProvider _historyProvider;

        public ProviderConversationRepository(ICliHistoryProvider historyProvider)
        {
            _historyProvider = historyProvider;
        }

        public Task<List<CliSessionInfo>> ListSessionsAsync()
        {
            return Task.FromResult(_historyProvider?.ListSessions() ?? new List<CliSessionInfo>());
        }

        public Task<Conversation> LoadConversationAsync(string sessionId)
        {
            return Task.FromResult(_historyProvider?.LoadConversation(sessionId));
        }

        public Task<ConversationUsageInfo> GetSessionUsageAsync(string sessionId)
        {
            return Task.FromResult(_historyProvider?.GetSessionUsage(sessionId));
        }
    }

    internal sealed class PersistentSessionConversationRepository : IConversationRepository, IDisposable
    {
        private readonly ISettingsStore _settingsStore;
        private readonly ILiveConversationSessionBackend _backend;
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private readonly Dictionary<string, ConversationUsageInfo> _usageCache = new(StringComparer.Ordinal);

        private bool _disposed;

        public PersistentSessionConversationRepository(ISettingsStore settingsStore, ILiveConversationSessionBackend backend)
        {
            _settingsStore = settingsStore;
            _backend = backend;
        }

        public async Task<List<CliSessionInfo>> ListSessionsAsync()
        {
            if (_disposed)
            {
                return new List<CliSessionInfo>();
            }

            await _operationGate.WaitAsync();
            try
            {
                return await _backend.ListSessionsAsync(GetWorkingDirectory()) ?? new List<CliSessionInfo>();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async Task<Conversation> LoadConversationAsync(string sessionId)
        {
            if (_disposed || string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            await _operationGate.WaitAsync();
            try
            {
                var replay = await _backend.LoadConversationAsync(sessionId, GetWorkingDirectory());
                if (replay.conversation == null)
                {
                    _usageCache.Remove(sessionId);
                    return null;
                }

                if (replay.usage != null)
                {
                    _usageCache[sessionId] = replay.usage;
                }
                else
                {
                    _usageCache.Remove(sessionId);
                }

                return replay.conversation;
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public Task<ConversationUsageInfo> GetSessionUsageAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Task.FromResult<ConversationUsageInfo>(null);
            }

            return Task.FromResult(_usageCache.TryGetValue(sessionId, out var usage) ? usage : null);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _operationGate.Dispose();
        }

        private string GetWorkingDirectory()
        {
            return string.IsNullOrWhiteSpace(_settingsStore?.WorkingDirectory)
                ? null
                : _settingsStore.WorkingDirectory;
        }
    }

    internal sealed class CliProviderCatalogAdapter : IProviderCatalog
    {
        private readonly IReadOnlyList<IProviderModule> _providers;

        public CliProviderCatalogAdapter()
        {
            _providers = CliProviderRegistry.AllProviders
                .Select(provider => (IProviderModule)new CliProviderModuleAdapter(provider))
                .ToList();
        }

        public IReadOnlyList<IProviderModule> AllProviders => _providers;

        public IProviderModule GetProvider(string id)
        {
            foreach (var provider in _providers)
            {
                if (string.Equals(provider.Id, id, StringComparison.Ordinal))
                {
                    return provider;
                }
            }

            return _providers.FirstOrDefault();
        }
    }

    internal sealed class CliProviderModuleAdapter : IProviderModule
    {
        private readonly ICliProvider _provider;
        private readonly IProviderUiMetadata _metadata;

        public CliProviderModuleAdapter(ICliProvider provider)
        {
            _provider = provider;
            _metadata = new ProviderUiMetadataAdapter(provider);
        }

        public string Id => _provider.Id;
        public IProviderUiMetadata Metadata => _metadata;

        public IProviderScope CreateScope(ISettingsStore settingsStore, IRuntimeLeaseManager leaseManager, ILogger logger)
        {
            var toolMapper = _provider.CreateToolMapper();
            var sessionBackend = (_provider as IPersistentSessionBackendFactory)?.CreateSessionBackend(settingsStore, toolMapper, logger);
            var lease = leaseManager.Acquire(this, settingsStore, logger, sessionBackend?.RuntimeClient);
            IDisposable ownedResource = null;
            IConversationRepository conversations;

            if (sessionBackend is IPersistentConversationHistoryBackend historyBackend)
            {
                var store = new PersistentConversationStore();
                ownedResource = new PersistentConversationRecorder(historyBackend, store, logger);
                conversations = new PersistentConversationStorageRepository(historyBackend.ProviderId, store, historyBackend);
            }
            else if (sessionBackend is ILiveConversationSessionBackend liveBackend)
            {
                conversations = new PersistentSessionConversationRepository(settingsStore, liveBackend);
            }
            else
            {
                conversations = new ProviderConversationRepository(_provider.CreateHistoryProvider());
            }

            var capabilities = (_provider as IProviderCapabilitySourcesFactory)
                ?.CreateCapabilitySources(settingsStore, logger);
            var modelCatalogSource = sessionBackend as IProviderModelCatalogSource
                ?? capabilities?.ModelCatalogSource;
            var slashCommandSource = sessionBackend as IProviderSlashCommandSource
                ?? capabilities?.SlashCommandSource;

            return new ProviderScope(
                lease,
                conversations,
                sessionBackend,
                modelCatalogSource,
                slashCommandSource,
                toolMapper,
                _metadata,
                ownedResource,
                capabilities);
        }
    }

    internal sealed class ProviderScope : IProviderScope
    {
        private readonly IRuntimeLease _lease;
        private readonly IDisposable _ownedResource;
        private readonly IDisposable _capabilities;

        public ProviderScope(
            IRuntimeLease lease,
            IConversationRepository conversations,
            IPersistentSessionBackend sessionBackend,
            IProviderModelCatalogSource modelCatalogSource,
            IProviderSlashCommandSource slashCommandSource,
            IProviderToolMapper toolMapper,
            IProviderUiMetadata metadata,
            IDisposable ownedResource = null,
            IDisposable capabilities = null)
        {
            _lease = lease;
            _ownedResource = ownedResource;
            _capabilities = capabilities;
            Runtime = lease?.Runtime;
            Conversations = conversations;
            SessionBackend = sessionBackend;
            ModelCatalogSource = modelCatalogSource;
            SlashCommandSource = slashCommandSource;
            ToolMapper = toolMapper;
            Metadata = metadata;
        }

        public IRuntimeOrchestrator Runtime { get; }
        public IConversationRepository Conversations { get; }
        public IPersistentSessionBackend SessionBackend { get; }
        public IProviderModelCatalogSource ModelCatalogSource { get; }
        public IProviderSlashCommandSource SlashCommandSource { get; }
        public IProviderToolMapper ToolMapper { get; }
        public IProviderUiMetadata Metadata { get; }

        public void Dispose()
        {
            if (Conversations is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _lease?.Dispose();
            SessionBackend?.Dispose();
            _ownedResource?.Dispose();
            _capabilities?.Dispose();
        }
    }

    internal sealed class WindowScopedRuntimeLeaseManager : IRuntimeLeaseManager
    {
        public IRuntimeLease Acquire(
            IProviderModule providerModule,
            ISettingsStore settingsStore,
            ILogger logger,
            ISessionRuntimeClient sharedSessionRuntimeClient = null)
        {
            return new WindowScopedRuntimeLease(new ProviderRuntimeOrchestrator(sharedSessionRuntimeClient, providerModule?.Id));
        }
    }

    internal sealed class WindowScopedRuntimeLease : IRuntimeLease
    {
        public WindowScopedRuntimeLease(IRuntimeOrchestrator runtime)
        {
            Runtime = runtime;
        }

        public IRuntimeOrchestrator Runtime { get; }

        public void Dispose()
        {
            Runtime?.Dispose();
        }
    }

    internal sealed class ProviderRuntimeOrchestrator : ProcessManager
    {
        public ProviderRuntimeOrchestrator(ISessionRuntimeClient sharedSessionRuntimeClient = null, string sharedSessionRuntimeProviderId = null)
            : base(sharedSessionRuntimeClient, sharedSessionRuntimeProviderId)
        {
        }
    }

    internal sealed class SessionStateResumeStateStore : IResumeStateStore
    {
        private const string PendingKey = "Sidekick.AutoResume.Pending";
        private const string SessionIdKey = "Sidekick.AutoResume.SessionId";
        private const string ProviderIdKey = "Sidekick.AutoResume.ProviderId";
        private const string HostTokenKey = "Sidekick.AutoResume.HostToken";
        private const string InputTextKey = "Sidekick.InputState.Text";
        private const string InputContextKey = "Sidekick.InputState.Context";
        private const string InputImagesKey = "Sidekick.InputState.Images";

        public void SavePendingResume(string hostToken, string providerId, string sessionId)
        {
            UnityEditor.SessionState.SetBool(PendingKey, true);
            UnityEditor.SessionState.SetString(HostTokenKey, hostToken ?? string.Empty);
            UnityEditor.SessionState.SetString(ProviderIdKey, providerId ?? string.Empty);
            UnityEditor.SessionState.SetString(SessionIdKey, sessionId ?? string.Empty);
        }

        public bool TryConsumePendingResume(out string hostToken, out string providerId, out string sessionId)
        {
            if (!UnityEditor.SessionState.GetBool(PendingKey, false))
            {
                hostToken = null;
                providerId = null;
                sessionId = null;
                return false;
            }

            hostToken = UnityEditor.SessionState.GetString(HostTokenKey, null);
            providerId = UnityEditor.SessionState.GetString(ProviderIdKey, null);
            sessionId = UnityEditor.SessionState.GetString(SessionIdKey, null);
            ClearPendingResume();
            return !string.IsNullOrEmpty(sessionId);
        }

        public void ClearPendingResume()
        {
            UnityEditor.SessionState.EraseBool(PendingKey);
            UnityEditor.SessionState.EraseString(HostTokenKey);
            UnityEditor.SessionState.EraseString(ProviderIdKey);
            UnityEditor.SessionState.EraseString(SessionIdKey);
        }

        public void SaveInputFieldState(InputFieldState state)
        {
            if (state == null)
            {
                UnityEditor.SessionState.EraseString(InputTextKey);
                UnityEditor.SessionState.EraseString(InputContextKey);
                UnityEditor.SessionState.EraseString(InputImagesKey);
                return;
            }

            UnityEditor.SessionState.SetString(InputTextKey, state.InputText ?? string.Empty);
            UnityEditor.SessionState.SetString(InputContextKey, Newtonsoft.Json.JsonConvert.SerializeObject(state.ContextAttachments ?? new List<SerializedContextAttachment>()));
            UnityEditor.SessionState.SetString(InputImagesKey, Newtonsoft.Json.JsonConvert.SerializeObject(state.ImageAttachments ?? new List<ImageAttachment>()));
        }

        public InputFieldState LoadAndClearInputFieldState()
        {
            var text = UnityEditor.SessionState.GetString(InputTextKey, null);
            var contextJson = UnityEditor.SessionState.GetString(InputContextKey, null);
            var imagesJson = UnityEditor.SessionState.GetString(InputImagesKey, null);

            UnityEditor.SessionState.EraseString(InputTextKey);
            UnityEditor.SessionState.EraseString(InputContextKey);
            UnityEditor.SessionState.EraseString(InputImagesKey);

            var hasText = !string.IsNullOrEmpty(text);
            var hasContext = !string.IsNullOrEmpty(contextJson) && contextJson != "[]";
            var hasImages = !string.IsNullOrEmpty(imagesJson) && imagesJson != "[]";
            if (!hasText && !hasContext && !hasImages)
            {
                return null;
            }

            var state = new InputFieldState
            {
                InputText = text ?? string.Empty
            };

            if (hasContext)
            {
                try
                {
                    state.ContextAttachments = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SerializedContextAttachment>>(contextJson)
                                               ?? new List<SerializedContextAttachment>();
                }
                catch
                {
                    state.ContextAttachments = new List<SerializedContextAttachment>();
                }
            }

            if (hasImages)
            {
                try
                {
                    state.ImageAttachments = Newtonsoft.Json.JsonConvert.DeserializeObject<List<ImageAttachment>>(imagesJson)
                                             ?? new List<ImageAttachment>();
                }
                catch
                {
                    state.ImageAttachments = new List<ImageAttachment>();
                }
            }

            return state;
        }
    }
}
