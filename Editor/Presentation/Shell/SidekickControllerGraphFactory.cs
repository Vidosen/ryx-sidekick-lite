// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Infrastructure.Mcp;
using Ryx.Sidekick.Editor.UseCases.Attachments;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.UseCases.Pro;
using Ryx.Sidekick.Editor.UseCases.Questions;
using Ryx.Sidekick.Editor.UseCases.Chat;
using Ryx.Sidekick.Editor.UseCases.Updates;
using Ryx.Sidekick.Editor.UseCases.Conversations;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Permissions;
using UnityEngine;
using Debug = UnityEngine.Debug;
using ILogger = Ryx.Sidekick.Editor.UseCases.Contracts.ILogger;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    internal sealed class SidekickControllerGraphFactory : ISidekickControllerGraphFactory
    {
        private readonly ISettingsStore _settingsStore;
        private readonly IEditorDialogService _dialogService;
        private readonly IEditorScheduler _scheduler;
        private readonly IClock _clock;
        private readonly IClipboardService _clipboardService;
        private readonly IAuthService _authService;
        private readonly ISidekickAccountService _sidekickAccountService;
        private readonly IProviderCatalog _providerCatalog;
        private readonly IRuntimeLeaseManager _runtimeLeaseManager;
        private readonly IMcpForUnityGateway _mcpGateway;
        private readonly IMarkdownContentRenderer _markdownContentRenderer;
        private readonly ILogger _logger;
        private readonly SidekickStoreService _storeService;
        private readonly IToolRendererRegistry _toolRendererRegistry;
        private readonly IAttachmentElementFactory _attachmentFactory;
        private readonly IDragDropAttachmentSource _dragDropSource;
        private readonly IViewScreenshotService _screenshotService;
        private readonly IEditorSelectionService _selectionService;
        private readonly IProPresence _proPresence;
        private readonly GetProOfferQuery _getProOffer;
        private readonly ResolveLockedProvidersQuery _resolveLockedProviders;
        private readonly PaywallViewModel _paywallViewModel;
        private readonly CheckForUpdatesQuery _checkForUpdates;
        private readonly IRemoteConfigSource _remoteConfigSource;
        private readonly IExternalUrlOpener _externalUrlOpener;
        private readonly IDismissStore _dismissStore;
        private readonly ResolveProAccessStateQuery _resolveProAccessState;
        private readonly IProEntitlement _proEntitlement;
        private readonly IUpdateInstaller _updateInstaller;

        public SidekickControllerGraphFactory(
            ISettingsStore settingsStore,
            IEditorDialogService dialogService,
            IEditorScheduler scheduler,
            IClock clock,
            IClipboardService clipboardService,
            IAuthService authService,
            ISidekickAccountService sidekickAccountService,
            IProviderCatalog providerCatalog,
            IRuntimeLeaseManager runtimeLeaseManager,
            IMcpForUnityGateway mcpGateway,
            IMarkdownContentRenderer markdownContentRenderer,
            ILogger logger,
            SidekickStoreService storeService,
            IToolRendererRegistry toolRendererRegistry,
            IAttachmentElementFactory attachmentFactory,
            IDragDropAttachmentSource dragDropSource,
            IViewScreenshotService screenshotService,
            IEditorSelectionService selectionService,
            IProPresence proPresence = null,
            GetProOfferQuery getProOffer = null,
            ResolveLockedProvidersQuery resolveLockedProviders = null,
            PaywallViewModel paywallViewModel = null,
            CheckForUpdatesQuery checkForUpdates = null,
            IRemoteConfigSource remoteConfigSource = null,
            IExternalUrlOpener externalUrlOpener = null,
            IDismissStore dismissStore = null,
            ResolveProAccessStateQuery resolveProAccessState = null,
            IProEntitlement proEntitlement = null,
            IUpdateInstaller updateInstaller = null)
        {
            _settingsStore = settingsStore;
            _dialogService = dialogService;
            _scheduler = scheduler;
            _clock = clock;
            _clipboardService = clipboardService;
            _authService = authService;
            _sidekickAccountService = sidekickAccountService;
            _providerCatalog = providerCatalog;
            _runtimeLeaseManager = runtimeLeaseManager;
            _mcpGateway = mcpGateway;
            _markdownContentRenderer = markdownContentRenderer;
            _logger = logger;
            _storeService = storeService;
            _toolRendererRegistry = toolRendererRegistry;
            _attachmentFactory = attachmentFactory;
            _dragDropSource = dragDropSource;
            _screenshotService = screenshotService;
            _selectionService = selectionService;
            _proPresence = proPresence;
            _getProOffer = getProOffer;
            _resolveLockedProviders = resolveLockedProviders;
            _paywallViewModel = paywallViewModel;
            _checkForUpdates = checkForUpdates;
            _remoteConfigSource = remoteConfigSource;
            _externalUrlOpener = externalUrlOpener;
            _dismissStore = dismissStore;
            _resolveProAccessState = resolveProAccessState;
            _proEntitlement = proEntitlement;
            _updateInstaller = updateInstaller;
        }

        public SidekickWindowScopeGraph CreateWindowScopeGraph(
            Func<IProviderUiMetadata> getActiveProviderMetadata)
        {
            // Window-scoped attachment state and use cases — see the matching note in
            // SidekickServiceRegistry. DI resolve via IServiceScope is deferred to T10/T11.
            var attachmentState = new AttachmentSessionState();
            var addImageUseCase = new AddImageAttachmentUseCase(attachmentState);
            var addContextUseCase = new AddContextAttachmentUseCase(attachmentState);
            var removeUseCase = new RemoveAttachmentUseCase(attachmentState);
            var attachmentController = new AttachmentController(
                _attachmentFactory,
                _screenshotService,
                _clipboardService,
                _dragDropSource,
                attachmentState,
                addImageUseCase,
                addContextUseCase,
                removeUseCase);
            var assetRefreshController = new AssetRefreshController(_scheduler);
            var providerMenuDisplayBinder = new ProviderMenuDisplayBinder(_providerCatalog, _storeService);
            var providerSelectorViewModel = new ProviderSelectorViewModel(
                _storeService,
                _providerCatalog,
                _settingsStore,
                _proPresence,
                _getProOffer,
                _resolveLockedProviders);
            var permissionService = new PermissionService((permission, allow, message, remember) => { }, _settingsStore);
            var resolvePermissionUseCase = new ResolvePermissionUseCase(
                permissionService,
                new AutoAcceptPermissionPolicy(),
                _settingsStore,
                getActiveProviderMetadata);
            var schemaRegistry = AskUserQuestionSchemaRegistry.CreateDefault(_settingsStore, _providerCatalog);
            var submitAskUserQuestionUseCase = new SubmitAskUserQuestionUseCase(schemaRegistry);
            var permissionController = new PermissionController(
                permissionService,
                _settingsStore,
                getActiveProviderMetadata,
                resolvePermissionUseCase);
            var permissionOverlayController = new PermissionOverlayController(
                permissionService,
                _storeService,
                resolvePermissionUseCase);
            var askUserQuestionController = new AskUserQuestionController(
                runtimeOrchestrator: null,
                settingsStore: _settingsStore,
                schemaRegistry: schemaRegistry,
                storeService: _storeService,
                submitUseCase: submitAskUserQuestionUseCase);
            var authController = new AuthController(
                _authService,
                _dialogService,
                _scheduler,
                _clipboardService,
                _settingsStore,
                _providerCatalog);
            var accountController = _sidekickAccountService != null
                ? new SidekickAccountController(_sidekickAccountService, _scheduler)
                : null;
            var mcpController = new McpForUnityController(_mcpGateway);

            var markdownContext = new MarkdownRenderContext
            {
                UseRichTextForInlines = true,
                MaxNestingDepth = 6,
                OnLinkClicked = url =>
                {
                    if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
                },
                OnCodeCopy = code =>
                {
                    if (!string.IsNullOrEmpty(code))
                    {
                        GUIUtility.systemCopyBuffer = code;
                        Debug.Log("[Ryx Sidekick] Code copied to clipboard");
                    }
                }
            };

            var messageElementFactory = new MessageElementFactory(
                _markdownContentRenderer,
                markdownContext,
                attachmentController,
                permissionController.IsToolUseAutoAccepted,
                _toolRendererRegistry);

            var chatTimelineViewModel = new ChatTimelineViewModel();

            return new SidekickWindowScopeGraph(
                _authService,
                attachmentController,
                assetRefreshController,
                providerMenuDisplayBinder,
                permissionService,
                permissionController,
                permissionOverlayController,
                askUserQuestionController,
                authController,
                mcpController,
                _markdownContentRenderer,
                markdownContext,
                messageElementFactory,
                providerSelectorViewModel,
                chatTimelineViewModel,
                _paywallViewModel,
                _proPresence,
                _checkForUpdates,
                _remoteConfigSource,
                _externalUrlOpener,
                _dismissStore,
                accountController,
                _resolveProAccessState,
                _proEntitlement,
                _updateInstaller,
                _sidekickAccountService);
        }

        public SidekickProviderScopeGraph CreateProviderScopeGraph(
            string providerId,
            SidekickWindowScopeGraph windowScopeGraph,
            Unity.AppUI.MVVM.IServiceScope providerServiceScope)
        {
            var providerModule = _providerCatalog.GetProvider(providerId);
            var providerScope = providerModule.CreateScope(_settingsStore, _runtimeLeaseManager, _logger);
            var loadConversationListQuery = new LoadConversationListQuery(
                providerScope.Conversations,
                providerScope.SessionBackend,
                _clock,
                providerScope.Metadata.DisplayName);
            var loadConversationHistoryUseCase = new LoadConversationHistoryUseCase(
                providerScope.Conversations,
                providerScope.SessionBackend,
                providerScope.Metadata.DisplayName);
            var selectConversationUseCase = new SelectConversationUseCase(
                _settingsStore,
                loadConversationHistoryUseCase);
            var conversationController = new ConversationController(
                loadConversationListQuery,
                loadConversationHistoryUseCase,
                selectConversationUseCase,
                providerScope.SessionBackend,
                providerScope.Metadata.DisplayName,
                _settingsStore,
                destroy => windowScopeGraph.AttachmentController?.ClearPendingAttachments(destroy));
            var chatSessionState = CreateChatSessionState(conversationController, providerScope.Runtime);
            var chatController = new ChatController(
                providerScope.Runtime,
                _settingsStore,
                conversationController,
                windowScopeGraph.AuthController,
                windowScopeGraph.AttachmentController,
                windowScopeGraph.PermissionController,
                windowScopeGraph.AssetRefreshController,
                (IChatTimelineSink)windowScopeGraph.ChatTimelineViewModel ?? NullChatTimelineSink.Instance,
                _dialogService,
                _scheduler,
                _storeService,
                chatSessionState: chatSessionState,
                sendPromptUseCase: new SendPromptUseCase(providerScope.Runtime, chatSessionState),
                stopTurnUseCase: new StopTurnUseCase(providerScope.Runtime, chatSessionState),
                clock: _clock);

            windowScopeGraph.AskUserQuestionController?.SetApplyAnswersToTimeline(chatController.ApplyAskUserQuestionTraceAnswers);
            windowScopeGraph.AskUserQuestionController?.SetSubmitLocalFollowup(chatController.SendLocalFollowupMessage);

            windowScopeGraph.ChatTimelineViewModel?.AttachProviderScope(
                chatSessionState,
                conversationController,
                retryHistoryAction: () =>
                {
                    var current = conversationController.CurrentConversation;
                    if (current != null)
                        _ = conversationController.SelectConversationAsync(current);
                    else
                        _ = conversationController.LoadConversationsAsync();
                });
            windowScopeGraph.ChatTimelineViewModel?.AttachChatController(chatController);

            var composerViewModel = new ComposerViewModel(
                _storeService,
                chatSessionState,
                windowScopeGraph.AttachmentController,
                _selectionService);
            chatController.BindComposerViewModel(composerViewModel);

            windowScopeGraph.PermissionOverlayController?.SetComposerViewModel(composerViewModel);
            windowScopeGraph.AskUserQuestionController?.SetComposerViewModel(composerViewModel);

            return new SidekickProviderScopeGraph(
                providerServiceScope,
                providerScope,
                conversationController,
                chatController,
                composerViewModel);
        }

        private ChatSessionState CreateChatSessionState(
            ConversationController conversationController,
            IRuntimeOrchestrator runtimeOrchestrator)
        {
            return new ChatSessionState(
                new ConversationControllerChatConversationSession(conversationController),
                _settingsStore,
                () => runtimeOrchestrator?.IsTurnInProgress ?? false);
        }
    }
}
