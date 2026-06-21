// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Presentation.Presenters;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.UseCases.Attachments;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Providers;
using Unity.AppUI.MVVM;
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// DI composition root for the Sidekick editor window.
    /// Owns the root <see cref="ServiceProvider"/> and provider scope lifecycle.
    ///
    /// Deliberately does NOT use App UI's <c>UIToolkitAppBuilder&lt;TApp&gt;</c> +
    /// <c>App</c> lifecycle: that pipeline is designed for runtime apps with their
    /// own root canvas/EventSystem and would double-init the DI container and the
    /// App UI <c>Panel</c> when hosted inside an EditorWindow. Sidekick instead
    /// constructs <see cref="ServiceCollection"/> + <see cref="ServiceProvider"/>
    /// directly (both are public types in <c>Unity.AppUI.MVVM</c>) and creates a
    /// scoped <see cref="SidekickAppPanel"/> sibling under the EditorWindow root.
    ///
    /// Locked by <c>ArchitectureBoundaryTests.NoType_DerivesFromAppUiAppOrUIToolkitAppBuilder</c>:
    /// adding another <c>UIToolkitAppBuilder</c> here would silently break window scope.
    ///
    /// Window UI binding lives in presentation presenters; this host owns only DI,
    /// provider-scope lifecycle, and application-level state persistence.
    /// </summary>
    internal sealed class SidekickEditorAppHost : ISidekickWindowHost
    {
        private readonly Func<string> _getHostToken;
        private readonly Action<string> _setHostToken;
        private readonly ISettingsStore _settingsStore;
        private readonly IEditorDialogService _dialogService;
        private readonly ISidekickControllerGraphFactory _controllerGraphFactory;
        private readonly SwitchProviderUseCase _switchProviderUseCase;
        private readonly LoadProviderUiStateUseCase _loadProviderUiStateUseCase;
        private readonly SaveProviderUiStateUseCase _saveProviderUiStateUseCase;
        private readonly Dictionary<string, ProviderDraftSnapshot> _draftSnapshots = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ProviderUiStateSnapshot> _providerUiStateSnapshots = new(StringComparer.Ordinal);

        private ServiceProvider _serviceProvider;
        private WindowViewBindingPresenter _viewBindingPresenter;
        private IProviderScope _activeProviderScope;
        private string _testHostToken;
        private string _scopedProviderId;
        private ActiveProviderStateSnapshot _scopedProviderState;
        private bool _initialized;
        private bool _disposed;
        private bool _suppressSettingsSync;

        internal IServiceProvider Services => _serviceProvider;

        internal IServiceScope ProviderScope { get; private set; }

        internal SidekickStoreService StoreService { get; }

        internal ComposerViewModel ComposerViewModel { get; private set; }

        internal ProviderSelectorViewModel ProviderSelectorViewModel => WindowScopeGraph?.ProviderSelectorViewModel;

        internal SidekickWindowScopeGraph WindowScopeGraph { get; private set; }

        internal ConversationController ConversationController { get; private set; }

        internal ChatController ChatController { get; private set; }

        internal IProviderSlashCommandSource ActiveSlashCommandSource => _activeProviderScope?.SlashCommandSource;

        internal event Action ProviderScopeChanged;

        /// <summary>
        /// Test-only constructor: builds the service provider internally, no window required.
        /// Use this ctor in unit tests that do not need UI Toolkit / window lifecycle.
        /// </summary>
        internal SidekickEditorAppHost()
            : this(getHostToken: null, setHostToken: null)
        {
        }

        /// <summary>
        /// Production constructor: called by <c>SidekickWindow.OnEnable</c> with delegates
        /// to the window's serialized host-token field.
        /// </summary>
        internal SidekickEditorAppHost(Func<string> getHostToken, Action<string> setHostToken)
        {
            _getHostToken = getHostToken;
            _setHostToken = setHostToken;

            var services = new ServiceCollection();
            SidekickServiceRegistry.RegisterWindowServices(services);
            _serviceProvider = services.BuildServiceProvider();

            _settingsStore = _serviceProvider.GetRequiredService<ISettingsStore>();
            _dialogService = _serviceProvider.GetRequiredService<IEditorDialogService>();
            _controllerGraphFactory = _serviceProvider.GetRequiredService<ISidekickControllerGraphFactory>();
            StoreService = _serviceProvider.GetRequiredService<SidekickStoreService>();
            _loadProviderUiStateUseCase = _serviceProvider.GetRequiredService<LoadProviderUiStateUseCase>();
            _saveProviderUiStateUseCase = _serviceProvider.GetRequiredService<SaveProviderUiStateUseCase>();
            _switchProviderUseCase = _serviceProvider.GetRequiredService<SwitchProviderUseCase>();
            _settingsStore.ActiveProviderStateChanged += HandleActiveProviderStateChanged;
        }

        /// <summary>
        /// Test-only constructor: all dependencies injected directly; no window required.
        /// </summary>
        internal SidekickEditorAppHost(
            ServiceProvider serviceProvider,
            ISettingsStore settingsStore,
            IEditorDialogService dialogService,
            ISidekickControllerGraphFactory controllerGraphFactory,
            SidekickStoreService storeService,
            LoadProviderUiStateUseCase loadProviderUiStateUseCase,
            SaveProviderUiStateUseCase saveProviderUiStateUseCase,
            SwitchProviderUseCase switchProviderUseCase)
        {
            _getHostToken = null;
            _setHostToken = null;
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _controllerGraphFactory = controllerGraphFactory ?? throw new ArgumentNullException(nameof(controllerGraphFactory));
            StoreService = storeService ?? throw new ArgumentNullException(nameof(storeService));
            _loadProviderUiStateUseCase = loadProviderUiStateUseCase ?? throw new ArgumentNullException(nameof(loadProviderUiStateUseCase));
            _saveProviderUiStateUseCase = saveProviderUiStateUseCase ?? throw new ArgumentNullException(nameof(saveProviderUiStateUseCase));
            _switchProviderUseCase = switchProviderUseCase ?? throw new ArgumentNullException(nameof(switchProviderUseCase));
            _settingsStore.ActiveProviderStateChanged += HandleActiveProviderStateChanged;
        }

        public string HostToken => GetOrCreateHostToken();

        public string CurrentProviderId => _scopedProviderState?.ProviderId ?? _settingsStore?.ProviderId;

        public string CurrentSessionId => ChatController?.IsTurnInProgress == true
            ? _activeProviderScope?.Runtime?.CurrentSessionId ?? ConversationController?.CurrentConversation?.SessionId
            : ConversationController?.CurrentConversation?.SessionId;

        public bool IsTurnActive => ChatController?.TurnActive == true;

        private string GetOrCreateHostToken()
        {
            var token = _getHostToken?.Invoke();
            if (string.IsNullOrEmpty(token))
            {
                token = string.IsNullOrEmpty(_testHostToken)
                    ? Guid.NewGuid().ToString("N")
                    : _testHostToken;

                _testHostToken = token;
                _setHostToken?.Invoke(token);
            }

            return token;
        }

        /// <summary>
        /// Creates a new provider scope, disposing the previous one if it exists.
        /// </summary>
        internal IServiceScope CreateProviderScope()
        {
            ProviderScope?.Dispose();
            ProviderScope = _serviceProvider.CreateScope();
            return ProviderScope;
        }

        internal void ReleaseProviderScope(IServiceScope scope)
        {
            if (scope == null)
            {
                return;
            }

            if (ReferenceEquals(ProviderScope, scope))
            {
                ProviderScope.Dispose();
                ProviderScope = null;
                return;
            }

            scope.Dispose();
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            WindowScopeGraph = _controllerGraphFactory.CreateWindowScopeGraph(
                () => _activeProviderScope?.Metadata);

            _viewBindingPresenter?.BindWindowScopeGraph(WindowScopeGraph);

            RebuildProviderScope(_settingsStore.CurrentActiveProviderState, restoreDraft: true);
            _initialized = true;

            // One-shot remote config fetch per Editor session (N5).
            // SessionState survives domain reloads so we never fetch more than once
            // per Unity session regardless of how many times Initialize is called.
            const string FetchedKey = "Sidekick_RemoteConfigFetched";
            if (!SessionState.GetBool(FetchedKey, false))
            {
                SessionState.SetBool(FetchedKey, true);
                var cfgSource = _serviceProvider?.GetService<IRemoteConfigSource>();
                if (cfgSource != null) _ = RefreshThenEvaluateUpdates(cfgSource);
            }
        }

        /// <summary>
        /// Awaits the remote-config refresh, then marshals a post-fetch update evaluation
        /// back to the Editor main thread via <c>delayCall</c>. Any exception from the
        /// network layer is swallowed so the window stays functional offline.
        /// </summary>
        private async System.Threading.Tasks.Task RefreshThenEvaluateUpdates(IRemoteConfigSource cfgSource)
        {
            try { await cfgSource.RefreshAsync(); }
            catch { /* network failure is non-fatal */ }

            EditorApplication.delayCall += () =>
            {
                try { _viewBindingPresenter?.UpdateNotificationViewModel?.Evaluate(); }
                catch { /* must not surface errors to the Editor main loop */ }

                // Update availability is unknown until the config lands → re-evaluate the status-bar
                // chip now so it can flip to "Update" when a newer Lite/Pro release is published.
                try { _viewBindingPresenter?.RefreshProChip(); }
                catch { /* must not surface errors to the Editor main loop */ }
            };
        }

        public void BindView(WindowViewBindingPresenter viewBindingPresenter)
        {
            _viewBindingPresenter = viewBindingPresenter;
            if (_viewBindingPresenter == null)
            {
                return;
            }

            _viewBindingPresenter.BindWindowScopeGraph(WindowScopeGraph);
            _viewBindingPresenter.RebindProviderScope(
                ConversationController,
                ChatController,
                ComposerViewModel);
        }

        public void OnFocus()
        {
            if (_activeProviderScope?.Runtime?.IsRunning == true)
            {
                return;
            }

            _ = ConversationController?.RefreshOnFocusAsync();
            WindowScopeGraph?.McpController?.OnWindowFocus();
        }

        public bool SwitchProvider(string providerId)
        {
            var outgoingProviderId = _scopedProviderId ?? _settingsStore.ProviderId;
            var outgoingDraftSnapshot = BuildCurrentDraftSnapshot();

            // Preserve the user's draft text and attachments for the outgoing provider so it
            // can be restored when switching back (plan "Preserve draft text" requirement).
            if (!string.IsNullOrWhiteSpace(outgoingProviderId) && outgoingDraftSnapshot != null)
            {
                _draftSnapshots[outgoingProviderId] = outgoingDraftSnapshot;
            }

            var request = new SwitchProviderRequest
            {
                TargetProviderId = providerId,
                CurrentProviderId = outgoingProviderId,
                IsTurnInProgress = ChatController?.IsTurnInProgress == true,
                SynchronizeSettings = true,
                OutgoingDraftSnapshot = outgoingDraftSnapshot,
                OutgoingProviderState = _scopedProviderState
            };

            var result = _switchProviderUseCase.Execute(request);

            switch (result.Status)
            {
                case SwitchProviderStatus.NoOpSameProvider:
                    return true;
                case SwitchProviderStatus.InvalidProvider:
                    return false;
                case SwitchProviderStatus.BlockedByActiveTurn:
                    _dialogService.DisplayDialog(result.RejectionDialogTitle, result.RejectionDialogMessage, "OK");
                    return false;
                case SwitchProviderStatus.Proceed:
                    RebuildProviderScope(result.ResolvedProviderState, restoreDraft: true);
                    return true;
                default:
                    return false;
            }
        }

        public void AutoResumeAfterDomainReload(string providerId, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(providerId) && !string.Equals(providerId, _settingsStore.ProviderId, StringComparison.Ordinal))
            {
                if (!SwitchProvider(providerId))
                {
                    return;
                }
            }

            ChatController?.AutoResumeAfterDomainReload(sessionId);
        }

        public InputFieldState CaptureInputFieldState()
        {
            return _viewBindingPresenter?.CaptureInputFieldState(WindowScopeGraph?.AttachmentController);
        }

        public void RestoreInputFieldState(InputFieldState state)
        {
            _viewBindingPresenter?.RestoreInputFieldState(state, WindowScopeGraph?.AttachmentController);
        }

        internal async Task InterruptRuntimeAsync()
        {
            try
            {
                if (_activeProviderScope?.Runtime?.IsRunning == true)
                {
                    await _activeProviderScope.Runtime.InterruptAsync();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _settingsStore.ActiveProviderStateChanged -= HandleActiveProviderStateChanged;

            CaptureCurrentDraftSnapshot();
            WindowScopeGraph?.ChatTimelineViewModel?.DetachChatController();
            WindowScopeGraph?.ChatTimelineViewModel?.DetachProviderScope();
            DisposeScopedControllers();

            WindowScopeGraph?.McpController?.Dispose();
            WindowScopeGraph?.AuthController?.Dispose();
            WindowScopeGraph?.AccountController?.Dispose();
            WindowScopeGraph?.ProviderMenuDisplayBinder?.Dispose();
            WindowScopeGraph?.Dispose();
            WindowScopeGraph = null;

            _viewBindingPresenter?.DetachScopeEventHandlers();
            _viewBindingPresenter = null;

            ProviderScope?.Dispose();
            ProviderScope = null;

            _serviceProvider?.Dispose();
            _serviceProvider = null;
        }

        private void RebuildProviderScope(
            ActiveProviderStateSnapshot acceptedProviderState,
            bool restoreDraft,
            bool synchronizeSettings = true)
        {
            var providerId = acceptedProviderState?.ProviderId;
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return;
            }

            WindowScopeGraph?.ChatTimelineViewModel?.DetachChatController();
            WindowScopeGraph?.ChatTimelineViewModel?.DetachProviderScope();
            DisposeScopedControllers();
            // Overlay resets dispatch to the store; run them after the outgoing scope is
            // disposed so the old provider scope (already unsubscribed by Dispose) does not
            // observe transient state-clear notifications during teardown.
            WindowScopeGraph?.PermissionOverlayController?.Reset();
            WindowScopeGraph?.AskUserQuestionController?.Reset();
            WindowScopeGraph?.PermissionController?.ResetPending();

            if (synchronizeSettings)
            {
                _suppressSettingsSync = true;
                try
                {
                    _settingsStore.ProviderId = providerId;
                }
                finally
                {
                    _suppressSettingsSync = false;
                }

                acceptedProviderState = _loadProviderUiStateUseCase.Execute(providerId);
            }

            var providerServiceScope = CreateProviderScope();
            var providerGraph = _controllerGraphFactory.CreateProviderScopeGraph(
                providerId,
                WindowScopeGraph,
                providerServiceScope);

            ProviderScope = providerGraph.ProviderServiceScope;
            _activeProviderScope = providerGraph.ProviderScope;
            ConversationController = providerGraph.ConversationController;
            ChatController = providerGraph.ChatController;
            ComposerViewModel = providerGraph.ComposerViewModel;
            _scopedProviderId = providerId;
            _scopedProviderState = acceptedProviderState;
            CacheProviderUiState(_settingsStore.GetProviderUiState(providerId));

            ChatController.SubscribeToProcessEvents();
            StoreService.ApplyScopedProviderSnapshot(_scopedProviderState);
            WindowScopeGraph?.ProviderSelectorViewModel?.BindModelCatalogSource(
                providerId,
                _activeProviderScope?.ModelCatalogSource);

            WindowScopeGraph.PermissionService.UpdateResponseCallback((permission, allow, message, remember) =>
            {
                _activeProviderScope?.Runtime?.SendPermissionResponse(permission, allow, message, remember);
            });
            WindowScopeGraph.AskUserQuestionController.UpdateRuntime(_activeProviderScope.Runtime);

            _viewBindingPresenter?.RebindProviderScope(
                ConversationController,
                ChatController,
                ComposerViewModel);
            _ = ConversationController.LoadConversationsAsync();

            if (restoreDraft)
            {
                RestoreDraftSnapshot(_draftSnapshots.GetValueOrDefault(providerId));
            }

            WindowScopeGraph?.AuthController?.UpdateAuthUI();
            ProviderScopeChanged?.Invoke();
        }

        private void CaptureCurrentDraftSnapshot()
        {
            if (_viewBindingPresenter == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_scopedProviderId) && ConversationController == null && ChatController == null)
            {
                return;
            }

            var providerId = string.IsNullOrWhiteSpace(_scopedProviderId)
                ? _settingsStore.ProviderId
                : _scopedProviderId;
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return;
            }

            var draftSnapshot = BuildCurrentDraftSnapshot() ?? new ProviderDraftSnapshot();
            _draftSnapshots[providerId] = draftSnapshot;
            var cachedSnapshot = GetCachedProviderUiState(providerId);

            var providerState = _scopedProviderState;
            if (providerState == null || !string.Equals(providerState.ProviderId, providerId, StringComparison.Ordinal))
            {
                providerState = _loadProviderUiStateUseCase.Execute(providerId);
            }

            var snapshot = _saveProviderUiStateUseCase.Execute(providerState, draftSnapshot, cachedSnapshot);
            if (snapshot != null)
            {
                CacheProviderUiState(snapshot);
            }
        }

        private ProviderDraftSnapshot BuildCurrentDraftSnapshot()
        {
            var attachmentController = WindowScopeGraph?.AttachmentController;
            return new ProviderDraftSnapshot
            {
                SelectedSessionId = ConversationController?.CurrentConversation?.SessionId,
                DraftText = _viewBindingPresenter?.ComposerView?.PromptText ?? string.Empty,
                DraftContextAttachments = ContextAttachmentSerializer.Serialize(attachmentController?.PendingContextAttachments),
                DraftImageAttachments = attachmentController?.PendingAttachments != null
                    ? new List<ImageAttachment>(attachmentController.PendingAttachments)
                    : new List<ImageAttachment>()
            };
        }

        private void RestoreDraftSnapshot(ProviderDraftSnapshot snapshot)
        {
            var composerView = _viewBindingPresenter?.ComposerView;
            var draftText = snapshot?.DraftText ?? string.Empty;
            if (composerView != null)
            {
                composerView.PromptText = draftText;
                composerView.AdjustForContent();
            }

            var attachmentController = WindowScopeGraph?.AttachmentController;
            if (snapshot == null)
            {
                attachmentController?.ClearPendingAttachments(destroyTextures: true);
                return;
            }

            var contextAttachments = ContextAttachmentSerializer.Deserialize(snapshot.DraftContextAttachments);
            attachmentController?.RestorePendingAttachments(snapshot.DraftImageAttachments, contextAttachments);
        }

        internal ProviderDraftSnapshot PeekDraftSnapshot(string providerId) =>
            _draftSnapshots.GetValueOrDefault(providerId);

        private void DisposeScopedControllers()
        {
            _viewBindingPresenter?.DetachScopeEventHandlers();

            if (ComposerViewModel != null)
            {
                ComposerViewModel.Dispose();
                ComposerViewModel = null;
            }

            if (ChatController != null)
            {
                ChatController.Dispose();
                ChatController = null;
            }

            ConversationController?.Dispose();
            ConversationController = null;

            if (_activeProviderScope != null)
            {
                _activeProviderScope.Dispose();
                _activeProviderScope = null;
            }

            _scopedProviderId = null;
            _scopedProviderState = null;

            if (ProviderScope != null)
            {
                ReleaseProviderScope(ProviderScope);
                ProviderScope = null;
            }

            _viewBindingPresenter?.RebindProviderScope(null, null, null);
            ProviderScopeChanged?.Invoke();
        }

        private void HandleActiveProviderStateChanged(ActiveProviderStateSnapshot snapshot)
        {
            if (_disposed || _suppressSettingsSync || !_initialized)
            {
                return;
            }

            var providerId = snapshot?.ProviderId;
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return;
            }

            if (string.Equals(providerId, _scopedProviderId, StringComparison.Ordinal))
            {
                _scopedProviderState = snapshot;
                // Use the local helper here (not SaveProviderUiStateUseCase) to update the in-memory
                // cache only — the use case writes to ISettingsStore which would double-save, since
                // this branch fires because ISettingsStore already emitted the change.
                CacheProviderUiState(CreateProviderUiStateSnapshot(snapshot, draftSnapshot: null, GetCachedProviderUiState(providerId)));
                WindowScopeGraph?.AuthController?.UpdateAuthUI();
                return;
            }

            if (ChatController?.IsTurnInProgress == true)
            {
                var rollbackRequest = new SwitchProviderRequest
                {
                    TargetProviderId = providerId,
                    CurrentProviderId = _scopedProviderId,
                    IsTurnInProgress = true,
                    SynchronizeSettings = false,
                    ExternalAcceptedState = snapshot
                };
                var rollbackResult = _switchProviderUseCase.Execute(rollbackRequest);

                // rollbackResult.Status is always BlockedByActiveTurn here.
                var scopedSnapshot = GetCachedProviderUiState(_scopedProviderId);
                _dialogService.DisplayDialog(
                    rollbackResult.RejectionDialogTitle,
                    rollbackResult.RejectionDialogMessage,
                    "OK");
                RestoreScopedProviderUiState();
                RestoreScopedProviderSelection(scopedSnapshot);
                if (scopedSnapshot != null)
                {
                    _saveProviderUiStateUseCase.Execute(_scopedProviderState, draftSnapshot: null, scopedSnapshot);
                }

                _scopedProviderState = _loadProviderUiStateUseCase.Execute(_scopedProviderId);
                CacheProviderUiState(_settingsStore.GetProviderUiState(_scopedProviderId));
                WindowScopeGraph?.AuthController?.UpdateAuthUI();
                return;
            }

            // Preserve the user's draft for the outgoing provider before tearing down the scope.
            // Mirrors the outgoing-save in SwitchProviderUseCase on the UI-driven path so the
            // outgoing provider's persisted UI state reflects the current draft session id.
            if (!string.IsNullOrWhiteSpace(_scopedProviderId))
            {
                var outgoingDraft = BuildCurrentDraftSnapshot();
                if (outgoingDraft != null)
                {
                    _draftSnapshots[_scopedProviderId] = outgoingDraft;
                }
                _saveProviderUiStateUseCase.Execute(
                    _scopedProviderState,
                    outgoingDraft,
                    GetCachedProviderUiState(_scopedProviderId));
            }

            RebuildProviderScope(snapshot, restoreDraft: true, synchronizeSettings: false);
        }

        private void RestoreScopedProviderSelection(ProviderUiStateSnapshot snapshot = null)
        {
            if (string.IsNullOrWhiteSpace(_scopedProviderId))
            {
                return;
            }

            _suppressSettingsSync = true;
            try
            {
                _settingsStore.ProviderId = _scopedProviderId;
                if (!string.IsNullOrWhiteSpace(snapshot?.SelectedSessionId)
                    && !string.Equals(_settingsStore.LastOpenedSessionId, snapshot.SelectedSessionId, StringComparison.Ordinal))
                {
                    _settingsStore.LastOpenedSessionId = snapshot.SelectedSessionId;
                }
            }
            finally
            {
                _suppressSettingsSync = false;
            }
        }

        private static ProviderUiStateSnapshot CreateProviderUiStateSnapshot(
            ActiveProviderStateSnapshot providerState,
            ProviderDraftSnapshot draftSnapshot,
            ProviderUiStateSnapshot fallbackSnapshot = null)
        {
            if (providerState == null || string.IsNullOrWhiteSpace(providerState.ProviderId))
            {
                return null;
            }

            return new ProviderUiStateSnapshot
            {
                ProviderId = providerState.ProviderId,
                SelectedSessionId = !string.IsNullOrWhiteSpace(draftSnapshot?.SelectedSessionId)
                    ? draftSnapshot.SelectedSessionId
                    : fallbackSnapshot?.SelectedSessionId ?? string.Empty,
                Model = providerState.Model,
                ReasoningEffort = providerState.ReasoningEffort,
                CollaborationMode = providerState.CollaborationMode,
                PermissionMode = providerState.PermissionMode
            };
        }

        private ProviderUiStateSnapshot GetCachedProviderUiState(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return null;
            }

            return _providerUiStateSnapshots.TryGetValue(providerId, out var snapshot)
                ? CloneProviderUiStateSnapshot(snapshot)
                : null;
        }

        private void CacheProviderUiState(ProviderUiStateSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ProviderId))
            {
                return;
            }

            _providerUiStateSnapshots[snapshot.ProviderId] = CloneProviderUiStateSnapshot(snapshot);
        }

        private void RestoreScopedProviderUiState()
        {
            var snapshot = GetCachedProviderUiState(_scopedProviderId);
            if (snapshot == null)
            {
                return;
            }

            _settingsStore.SaveProviderUiState(snapshot);
        }

        private static ProviderUiStateSnapshot CloneProviderUiStateSnapshot(ProviderUiStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            return new ProviderUiStateSnapshot
            {
                ProviderId = snapshot.ProviderId,
                SelectedSessionId = snapshot.SelectedSessionId,
                Model = snapshot.Model,
                ReasoningEffort = snapshot.ReasoningEffort,
                CollaborationMode = snapshot.CollaborationMode,
                PermissionMode = snapshot.PermissionMode
            };
        }
    }
}
