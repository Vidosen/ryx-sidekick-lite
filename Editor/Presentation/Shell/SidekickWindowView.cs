// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Views;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Thin UI Toolkit view wrapper for <see cref="SidekickWindow"/>.
    /// Owns UXML cloning and caching of VisualElement references.
    /// </summary>
    internal sealed class SidekickWindowView
    {
        public VisualElement Root { get; }

        public ChatTimelineView ChatTimeline { get; }

        public ComposerView Composer { get; }

        public StatusBarView StatusBar { get; }

        public ProviderMenuView ProviderMenu { get; }

        public AttachmentMenuView AttachmentMenu { get; }

        public ConversationMenuView ConversationMenu { get; }

        public LoginOverlayView LoginOverlayView { get; }

        public PermissionOverlayView PermissionOverlayView { get; }

        public AskUserQuestionView AskUserQuestionView { get; }

        public OnboardingView Onboarding { get; }

        public ImageOverlayView ImageOverlayView { get; }

        public INotificationPresenter Notifications { get; private set; }

        /// <summary>
        /// When true, the message list auto-scrolls as new content arrives.
        /// This is typically disabled when the user scrolls up to read older messages.
        /// </summary>
        public bool AutoScrollEnabled { get; set; } = true;

        // Main UI
        public ListView MessageListView { get; }
        public VisualElement ScrollToBottomContainer { get; }
        public Button ScrollToBottomButton { get; }
        public VisualElement WelcomeScreen { get; }
        public VisualElement InputWrapper { get; }
        public TextField InputField { get; }
        public VisualElement AttachmentsPreview { get; }
        public Unity.AppUI.UI.Button SendButton { get; }
        public Button NewChatButton { get; }

        // Provider selector
        public Unity.AppUI.UI.Button ProviderButton { get; }

        // Model selector
        public Unity.AppUI.UI.Button ModelButton { get; }

        // MCP for Unity section (status bar)
        public VisualElement McpSection { get; }
        public VisualElement McpIndicator { get; }
        public Label McpStatusText { get; }
        public Unity.AppUI.UI.Button McpButton { get; }
        public Unity.AppUI.UI.Button ProUpgradeChip { get; }

        // Login overlay
        public VisualElement LoginOverlayContainer { get; }
        public VisualElement LoginOverlay { get; }
        public VisualElement LoginSelectionScreen { get; }
        public VisualElement LoginOAuthScreen { get; }
        public VisualElement LoginAuthLostScreen { get; }
        public Button LoginClaudeAiButton { get; }
        public Button LoginConsoleButton { get; }
        public Button LoginThirdPartyButton { get; }
        public TextField OAuthUrlField { get; }
        public TextField OAuthCodeInput { get; }
        public Button OAuthCopyButton { get; }
        public Button OAuthContinueButton { get; }
        public Button OAuthBackButton { get; }
        public Button LoginAgainButton { get; }
        public Button LoginSwitchProviderButton { get; }

        // Conversation dropdown
        public Button ConversationDropdownButton { get; }
        public Label DropdownTitle { get; }
        public VisualElement ConversationPopup { get; }
        public TextField ConversationSearch { get; }
        public ScrollView PopupConversationList { get; }

        // Collaboration + permission mode controls
        public Button CollaborationModeButton { get; }
        public Label CollaborationModeLabel { get; }
        public Button EditModeButton { get; }
        public Label EditModeLabel { get; }
        public Button PermissionModeButton => EditModeButton;
        public Label PermissionModeLabel => EditModeLabel;
        public VisualElement ContextIndicator { get; }
        public Label ContextText { get; }

        // Context usage widget
        public VisualElement ContextUsage { get; }
        public VisualElement ContextUsagePie { get; }
        public Label ContextUsageText { get; }

        // Context attachments
        public VisualElement ContextChipsArea { get; }
        public Unity.AppUI.UI.Button AddContextButton { get; }

        // Image overlay
        public VisualElement ImageOverlay { get; }
        public VisualElement ImageOverlayBackdrop { get; }
        public VisualElement ImageOverlayContent { get; }
        public Button ImageOverlayClose { get; }
        public VisualElement ImageOverlayViewport { get; }
        public Image ImageOverlayImage { get; }

        // Onboarding overlay content fragment (cached off-tree until shown via Modal).
        private readonly VisualElement _onboardingContentFragment;

        // Notification presenter — owns transient toasts on the App UI notification layer
        // (refresh hint, etc.). Mounted in T08-06. Exposed via the Notifications property
        // so window-level consumers can talk to it directly without going through views.
        private readonly NotificationPresenter _notificationPresenter;

        private readonly ListView _messageListView;
        private readonly VisualElement _historyStatusHost;
        private readonly VisualElement _permissionBannerHost;

        private SidekickWindowView(
            VisualElement root,
            VisualElement scrollToBottomContainer,
            Button scrollToBottomButton,
            VisualElement welcomeScreen,
            VisualElement inputWrapper,
            TextField inputField,
            VisualElement attachmentsPreview,
            Unity.AppUI.UI.Button sendBtn,
            Button newChatBtn,
            Unity.AppUI.UI.Button providerBtn,
            Unity.AppUI.UI.Button modelBtn,
            VisualElement mcpSection,
            VisualElement mcpIndicator,
            Label mcpStatusText,
            Unity.AppUI.UI.Button mcpBtn,
            Unity.AppUI.UI.Button proUpgradeChip,
            VisualElement loginOverlayContainer,
            VisualElement loginOverlay,
            VisualElement loginSelectionScreen,
            VisualElement loginOAuthScreen,
            VisualElement loginAuthLostScreen,
            Button loginClaudeAiBtn,
            Button loginConsoleBtn,
            Button loginThirdPartyBtn,
            TextField oauthUrlField,
            TextField oauthCodeInput,
            Button oauthCopyBtn,
            Button oauthContinueBtn,
            Button oauthBackBtn,
            Button loginAgainBtn,
            Button loginSwitchProviderBtn,
            Button conversationDropdownBtn,
            Label dropdownTitle,
            VisualElement conversationPopup,
            TextField conversationSearch,
            ScrollView popupConversationList,
            Button collaborationModeBtn,
            Label collaborationModeLabel,
            Button editModeBtn,
            Label editModeLabel,
            VisualElement contextIndicator,
            Label contextText,
            VisualElement contextUsage,
            VisualElement contextUsagePie,
            Label contextUsageText,
            VisualElement contextChipsArea,
            Unity.AppUI.UI.Button addContextBtn,
            VisualElement imageOverlay,
            VisualElement imageOverlayBackdrop,
            VisualElement imageOverlayContent,
            Button imageOverlayClose,
            VisualElement imageOverlayViewport,
            Image imageOverlayImage,
            // Permission overlay — inline container + per-element refs
            VisualElement permissionContainer,
            VisualElement permissionOverlay,
            VisualElement permissionBackdrop,
            Label permissionIcon,
            Label permissionTitle,
            Label permissionCounter,
            Button permissionCloseBtn,
            Label permissionToolName,
            VisualElement permissionPathRow,
            Label permissionPath,
            VisualElement permissionCommandRow,
            Label permissionCommand,
            ScrollView permissionPreviewScroll,
            VisualElement permissionPreview,
            Button permissionShowMoreBtn,
            Label permissionReason,
            Button permissionDenyBtn,
            Button permissionDenyRememberBtn,
            Button permissionAllowBtn,
            Button permissionRememberBtn,
            // AskUserQuestion overlay — inline container + per-element refs
            VisualElement askContainer,
            VisualElement askOverlay,
            VisualElement askBackdrop,
            Label askHeaderText,
            VisualElement askTabs,
            Button askCloseBtn,
            Label askQuestionText,
            VisualElement askOptions,
            VisualElement askOtherContainer,
            TextField askOtherInput,
            VisualElement askFooter,
            Label askCountBadge,
            Button askSubmitBtn,
            // Onboarding overlay (content fragment owned by SidekickWindowView; child refs
            // are cached internally by OnboardingView, not re-exposed here)
            VisualElement onboardingContentFragment,
            ListView messageListView = null,
            VisualElement historyStatusHost = null,
            VisualElement permissionBannerHost = null)
        {
            Root = root;
            _messageListView = messageListView;
            _historyStatusHost = historyStatusHost;
            _permissionBannerHost = permissionBannerHost;
            MessageListView = messageListView;
            ScrollToBottomContainer = scrollToBottomContainer;
            ScrollToBottomButton = scrollToBottomButton;
            WelcomeScreen = welcomeScreen;
            InputWrapper = inputWrapper;
            InputField = inputField;
            AttachmentsPreview = attachmentsPreview;
            SendButton = sendBtn;
            NewChatButton = newChatBtn;

            ProviderButton = providerBtn;

            ModelButton = modelBtn;

            McpSection = mcpSection;
            McpIndicator = mcpIndicator;
            McpStatusText = mcpStatusText;
            McpButton = mcpBtn;
            ProUpgradeChip = proUpgradeChip;

            LoginOverlayContainer = loginOverlayContainer;
            LoginOverlay = loginOverlay;
            LoginSelectionScreen = loginSelectionScreen;
            LoginOAuthScreen = loginOAuthScreen;
            LoginAuthLostScreen = loginAuthLostScreen;
            LoginClaudeAiButton = loginClaudeAiBtn;
            LoginConsoleButton = loginConsoleBtn;
            LoginThirdPartyButton = loginThirdPartyBtn;
            OAuthUrlField = oauthUrlField;
            OAuthCodeInput = oauthCodeInput;
            OAuthCopyButton = oauthCopyBtn;
            OAuthContinueButton = oauthContinueBtn;
            OAuthBackButton = oauthBackBtn;
            LoginAgainButton = loginAgainBtn;
            LoginSwitchProviderButton = loginSwitchProviderBtn;

            ConversationDropdownButton = conversationDropdownBtn;
            DropdownTitle = dropdownTitle;
            ConversationPopup = conversationPopup;
            ConversationSearch = conversationSearch;
            PopupConversationList = popupConversationList;

            CollaborationModeButton = collaborationModeBtn;
            CollaborationModeLabel = collaborationModeLabel;
            EditModeButton = editModeBtn;
            EditModeLabel = editModeLabel;
            ContextIndicator = contextIndicator;
            ContextText = contextText;

            ContextUsage = contextUsage;
            ContextUsagePie = contextUsagePie;
            ContextUsageText = contextUsageText;

            ContextChipsArea = contextChipsArea;
            AddContextButton = addContextBtn;

            ImageOverlay = imageOverlay;
            ImageOverlayBackdrop = imageOverlayBackdrop;
            ImageOverlayContent = imageOverlayContent;
            ImageOverlayClose = imageOverlayClose;
            ImageOverlayViewport = imageOverlayViewport;
            ImageOverlayImage = imageOverlayImage;

            // Onboarding overlay content fragment (children are cached by OnboardingView)
            _onboardingContentFragment = onboardingContentFragment;

            PermissionOverlayView = new PermissionOverlayView(
                permissionContainer,
                permissionOverlay,
                permissionBackdrop,
                permissionIcon,
                permissionTitle,
                permissionCounter,
                permissionCloseBtn,
                permissionToolName,
                permissionPathRow,
                permissionPath,
                permissionCommandRow,
                permissionCommand,
                permissionPreviewScroll,
                permissionPreview,
                permissionShowMoreBtn,
                permissionReason,
                permissionDenyBtn,
                permissionDenyRememberBtn,
                permissionAllowBtn,
                permissionRememberBtn);
            AskUserQuestionView = new AskUserQuestionView(
                askContainer,
                askOverlay,
                askBackdrop,
                askHeaderText,
                askTabs,
                askCloseBtn,
                askQuestionText,
                askOptions,
                askOtherContainer,
                askOtherInput,
                askFooter,
                askCountBadge,
                askSubmitBtn,
                null);

            // Reference view = Root; the presenter routes Toast.FindSuitableParent up to
            // Panel.notificationContainer. Exposed via the Notifications property for
            // window-level consumers (refresh-asset hint, etc.).
            _notificationPresenter = new NotificationPresenter(Root);
            Notifications = _notificationPresenter;

            ChatTimeline = new ChatTimelineView(
                WelcomeScreen,
                ScrollToBottomContainer,
                ScrollToBottomButton,
                _messageListView,
                _historyStatusHost,
                _permissionBannerHost);
            Composer = new ComposerView(
                InputField,
                SendButton,
                NewChatButton,
                AddContextButton,
                AttachmentsPreview,
                ContextChipsArea);
            StatusBar = new StatusBarView(
                McpIndicator,
                McpStatusText,
                McpButton,
                ContextIndicator,
                ContextText,
                ContextUsage,
                ContextUsagePie,
                ContextUsageText,
                ProUpgradeChip);
            ConversationMenu = new ConversationMenuView(
                ConversationDropdownButton,
                ConversationPopup,
                ConversationSearch,
                PopupConversationList);
            LoginOverlayView = new LoginOverlayView(
                LoginOverlayContainer,
                LoginOverlay,
                LoginSelectionScreen,
                LoginOAuthScreen,
                LoginAuthLostScreen,
                LoginClaudeAiButton,
                LoginConsoleButton,
                LoginThirdPartyButton,
                OAuthUrlField,
                OAuthCodeInput,
                OAuthCopyButton,
                OAuthContinueButton,
                OAuthBackButton,
                LoginAgainButton,
                LoginSwitchProviderButton);
            var providerPopoverTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                SidekickUiConstants.ProviderPopoverContentUxmlPath);
            var modelPopoverTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                SidekickUiConstants.ModelPopoverContentUxmlPath);
            var attachmentMenuTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                SidekickUiConstants.AttachmentMenuContentUxmlPath);

            ProviderMenu = new ProviderMenuView(
                ProviderButton,
                providerPopoverTemplate,
                ModelButton,
                modelPopoverTemplate,
                CollaborationModeButton,
                CollaborationModeLabel,
                PermissionModeButton,
                PermissionModeLabel);
            AttachmentMenu = new AttachmentMenuView(
                AddContextButton,
                attachmentMenuTemplate);
            // Reference view = Root, so Modal.FindSuitableParent climbs to the App UI Panel.
            // contentFragment is the cached onboarding UXML instance owned by SidekickWindowView.
            Onboarding = new OnboardingView(Root, _onboardingContentFragment);
            ImageOverlayView = new ImageOverlayView(
                ImageOverlay,
                ImageOverlayBackdrop,
                ImageOverlayContent,
                ImageOverlayClose,
                ImageOverlayViewport,
                ImageOverlayImage);
        }

        public static bool TryCreate(
            VisualElement root,
            string uxmlPath,
            string loginOverlayUxmlPath,
            string askUserQuestionOverlayUxmlPath,
            string permissionOverlayUxmlPath,
            string onboardingOverlayUxmlPath,
            string logoAssetPath,
            string assetsPath,
            out SidekickWindowView view)
        {
            view = null;
            if (root == null) return false;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (!visualTree)
            {
                root.Add(new Label($"Failed to load UI: {uxmlPath}"));
                return false;
            }

            // Clear any existing children to make CreateGUI idempotent (Unity may call it multiple times)
            root.Clear();
            visualTree.CloneTree(root);

            // Login overlay - load dynamically
            var loginOverlayContainer = root.Q<VisualElement>("login-overlay-container");
            if (loginOverlayContainer != null)
            {
                var loginOverlayAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(loginOverlayUxmlPath);
                if (loginOverlayAsset != null)
                {
                    loginOverlayAsset.CloneTree(loginOverlayContainer);
                }
            }

            // Permission overlay — clone into inline container; visible state controlled via
            // style.display on the container and overlay elements.
            var permissionContainer = root.Q<VisualElement>("permission-overlay-container");
            if (permissionContainer != null && !string.IsNullOrEmpty(permissionOverlayUxmlPath))
            {
                var permissionOverlayTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(permissionOverlayUxmlPath);
                if (permissionOverlayTemplate != null)
                {
                    permissionOverlayTemplate.CloneTree(permissionContainer);
                    permissionContainer.style.display = DisplayStyle.Flex;
                }
            }

            // AskUserQuestion overlay — clone into inline container; visible state controlled via
            // style.display on the container and overlay elements.
            var askContainer = root.Q<VisualElement>("ask-user-question-container");
            if (askContainer != null && !string.IsNullOrEmpty(askUserQuestionOverlayUxmlPath))
            {
                var askOverlayTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(askUserQuestionOverlayUxmlPath);
                if (askOverlayTemplate != null)
                {
                    askOverlayTemplate.CloneTree(askContainer);
                    askContainer.style.display = DisplayStyle.Flex;
                }
            }

            // Onboarding overlay - load template; OnboardingView mounts the instantiated
            // content fragment via App UI Modal at Show() time.
            var onboardingOverlayTemplate = !string.IsNullOrEmpty(onboardingOverlayUxmlPath)
                ? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(onboardingOverlayUxmlPath)
                : null;

            // Cache UI elements
            var scrollToBottomContainer = root.Q<VisualElement>("scroll-to-bottom-container");
            var scrollToBottomButton = root.Q<Button>("scroll-to-bottom-btn");
            var welcomeScreen = root.Q<VisualElement>("welcome-screen");
            var inputWrapper = root.Q<VisualElement>(className: "sk-input-wrapper");
            var inputField = root.Q<TextField>("input-field");
            var attachmentsPreview = root.Q<VisualElement>("attachments-preview");
            var sendBtn = root.Q<Unity.AppUI.UI.Button>("send-btn");
            var newChatBtn = root.Q<Button>("new-chat-btn");

            // Provider selector
            var providerBtn = root.Q<Unity.AppUI.UI.Button>("provider-btn");

            // Model selector
            var modelBtn = root.Q<Unity.AppUI.UI.Button>("model-btn");

            var mcpSection = root.Q<VisualElement>("mcp-section");
            var mcpIndicator = root.Q<VisualElement>("mcp-indicator");
            var mcpStatusText = root.Q<Label>("mcp-status-text");
            var mcpBtn = root.Q<Unity.AppUI.UI.Button>("mcp-btn");
            var proUpgradeChip = root.Q<Unity.AppUI.UI.Button>("pro-upgrade-chip");

            var loginOverlay = loginOverlayContainer?.Q<VisualElement>("login-overlay");
            var loginSelectionScreen = loginOverlay?.Q<VisualElement>("login-selection-screen");
            var loginOAuthScreen = loginOverlay?.Q<VisualElement>("login-oauth-screen");
            var loginAuthLostScreen = loginOverlay?.Q<VisualElement>("login-auth-lost-screen");
            var loginClaudeAiBtn = loginOverlay?.Q<Button>("login-claude-ai-btn");
            var loginConsoleBtn = loginOverlay?.Q<Button>("login-console-btn");
            var loginThirdPartyBtn = loginOverlay?.Q<Button>("login-third-party-btn");
            var oauthUrlField = loginOverlay?.Q<TextField>("oauth-url-field");
            var oauthCodeInput = loginOverlay?.Q<TextField>("oauth-code-input");
            var oauthCopyBtn = loginOverlay?.Q<Button>("oauth-copy-btn");
            var oauthContinueBtn = loginOverlay?.Q<Button>("oauth-continue-btn");
            var oauthBackBtn = loginOverlay?.Q<Button>("oauth-back-btn");
            var loginAgainBtn = loginOverlay?.Q<Button>("login-again-btn");
            var loginSwitchProviderBtn = loginOverlay?.Q<Button>("login-switch-provider-btn");

            var conversationDropdownBtn = root.Q<Button>("conversation-dropdown-btn");
            var dropdownTitle = root.Q<Label>("dropdown-title");
            var conversationPopup = root.Q<VisualElement>("conversation-popup");
            var conversationSearch = root.Q<TextField>("conversation-search");
            var popupConversationList = root.Q<ScrollView>("popup-conversation-list");

            var collaborationModeBtn = root.Q<Button>("collaboration-mode-btn");
            var collaborationModeLabel = root.Q<Label>("collaboration-mode-label");
            var editModeBtn = root.Q<Button>("edit-mode-btn");
            var editModeLabel = root.Q<Label>("edit-mode-label");
            var contextIndicator = root.Q<VisualElement>("context-indicator");
            var contextText = root.Q<Label>("context-text");

            var contextUsage = root.Q<VisualElement>("context-usage");
            var contextUsagePie = root.Q<VisualElement>("context-usage-pie");
            var contextUsageText = root.Q<Label>("context-usage-text");

            var contextChipsArea = root.Q<VisualElement>("context-chips-area");
            var addContextBtn = root.Q<Unity.AppUI.UI.Button>("add-context-btn");

            var imageOverlay = root.Q<VisualElement>("image-overlay");
            var imageOverlayBackdrop = root.Q<VisualElement>("image-overlay-backdrop");
            var imageOverlayContent = root.Q<VisualElement>("image-overlay-content");
            var imageOverlayClose = root.Q<Button>("image-overlay-close");
            var imageOverlayViewport = root.Q<VisualElement>("image-overlay-viewport");
            var imageOverlayImage = root.Q<Image>("image-overlay-img");

            // ListView path elements
            var messageListView = root.Q<ListView>("message-list-view");
            var historyStatusHost = root.Q<VisualElement>("history-status-host");
            var permissionBannerHost = root.Q<VisualElement>("permission-banner-host");

            // Permission overlay — cache per-element refs from the cloned tree.
            var permissionOverlay = permissionContainer?.Q<VisualElement>("permission-overlay");
            var permissionBackdrop = permissionContainer?.Q<VisualElement>("perm-overlay-backdrop");
            var permissionIcon = permissionContainer?.Q<Label>("perm-icon");
            var permissionTitle = permissionContainer?.Q<Label>("perm-title");
            var permissionCounter = permissionContainer?.Q<Label>("perm-counter");
            var permissionCloseBtn = permissionContainer?.Q<Button>("perm-close-btn");
            var permissionToolName = permissionContainer?.Q<Label>("perm-tool-name");
            var permissionPathRow = permissionContainer?.Q<VisualElement>("perm-path-row");
            var permissionPath = permissionContainer?.Q<Label>("perm-path");
            var permissionCommandRow = permissionContainer?.Q<VisualElement>("perm-command-row");
            var permissionCommand = permissionContainer?.Q<Label>("perm-command");
            var permissionPreviewScroll = permissionContainer?.Q<ScrollView>("perm-preview-scroll");
            var permissionPreview = permissionContainer?.Q<VisualElement>("perm-preview");
            var permissionShowMoreBtn = permissionContainer?.Q<Button>("perm-show-more-btn");
            var permissionReason = permissionContainer?.Q<Label>("perm-reason");
            var permissionDenyBtn = permissionContainer?.Q<Button>("perm-deny-btn");
            var permissionDenyRememberBtn = permissionContainer?.Q<Button>("perm-deny-remember-btn");
            var permissionAllowBtn = permissionContainer?.Q<Button>("perm-allow-btn");
            var permissionRememberBtn = permissionContainer?.Q<Button>("perm-remember-btn");

            // AskUserQuestion overlay — cache per-element refs from the cloned tree.
            var askOverlay = askContainer?.Q<VisualElement>("ask-user-question-overlay");
            var askBackdrop = askContainer?.Q<VisualElement>("ask-overlay-backdrop");
            var askHeaderText = askContainer?.Q<Label>("ask-header-text");
            var askTabs = askContainer?.Q<VisualElement>("ask-tabs");
            var askCloseBtn = askContainer?.Q<Button>("ask-close-btn");
            var askQuestionText = askContainer?.Q<Label>("ask-question-text");
            var askOptions = askContainer?.Q<VisualElement>("ask-options");
            var askOtherContainer = askContainer?.Q<VisualElement>("ask-other-container");
            var askOtherInput = askContainer?.Q<TextField>("ask-other-input");
            var askFooter = askContainer?.Q<VisualElement>("ask-footer");
            var askCountBadge = askContainer?.Q<Label>("ask-count-badge");
            var askSubmitBtn = askContainer?.Q<Button>("ask-submit-btn");

            // Onboarding overlay content fragment — instantiated from template; lives off-tree
            // until the App UI Modal mounts it on Show(). OnboardingView caches its own child
            // element references via Q<>; SidekickWindowView only retains the fragment root.
            var onboardingContentInstance = onboardingOverlayTemplate?.Instantiate();
            var onboardingContent = onboardingContentInstance?.contentContainer ?? onboardingContentInstance;

            view = new SidekickWindowView(
                root,
                scrollToBottomContainer,
                scrollToBottomButton,
                welcomeScreen,
                inputWrapper,
                inputField,
                attachmentsPreview,
                sendBtn,
                newChatBtn,
                providerBtn,
                modelBtn,
                mcpSection,
                mcpIndicator,
                mcpStatusText,
                mcpBtn,
                proUpgradeChip,
                loginOverlayContainer,
                loginOverlay,
                loginSelectionScreen,
                loginOAuthScreen,
                loginAuthLostScreen,
                loginClaudeAiBtn,
                loginConsoleBtn,
                loginThirdPartyBtn,
                oauthUrlField,
                oauthCodeInput,
                oauthCopyBtn,
                oauthContinueBtn,
                oauthBackBtn,
                loginAgainBtn,
                loginSwitchProviderBtn,
                conversationDropdownBtn,
                dropdownTitle,
                conversationPopup,
                conversationSearch,
                popupConversationList,
                collaborationModeBtn,
                collaborationModeLabel,
                editModeBtn,
                editModeLabel,
                contextIndicator,
                contextText,
                contextUsage,
                contextUsagePie,
                contextUsageText,
                contextChipsArea,
                addContextBtn,
                imageOverlay,
                imageOverlayBackdrop,
                imageOverlayContent,
                imageOverlayClose,
                imageOverlayViewport,
                imageOverlayImage,
                // Permission overlay — inline container + per-element refs
                permissionContainer,
                permissionOverlay,
                permissionBackdrop,
                permissionIcon,
                permissionTitle,
                permissionCounter,
                permissionCloseBtn,
                permissionToolName,
                permissionPathRow,
                permissionPath,
                permissionCommandRow,
                permissionCommand,
                permissionPreviewScroll,
                permissionPreview,
                permissionShowMoreBtn,
                permissionReason,
                permissionDenyBtn,
                permissionDenyRememberBtn,
                permissionAllowBtn,
                permissionRememberBtn,
                // AskUserQuestion overlay — inline container + per-element refs
                askContainer,
                askOverlay,
                askBackdrop,
                askHeaderText,
                askTabs,
                askCloseBtn,
                askQuestionText,
                askOptions,
                askOtherContainer,
                askOtherInput,
                askFooter,
                askCountBadge,
                askSubmitBtn,
                // Onboarding overlay content fragment
                onboardingContent,
                // ListView path elements
                messageListView,
                historyStatusHost,
                permissionBannerHost);

            // Setup welcome screen images
            SetupWelcomeScreenImages(root, logoAssetPath);

            // Setup login overlay images
            SetupLoginOverlayImages(loginOverlay, logoAssetPath);

            // Set input field placeholder
            if (inputField != null)
            {
                inputField.textEdition.placeholder = "⌘ Esc to focus or unfocus Sidekick";
            }

            return true;
        }

        private static void SetupWelcomeScreenImages(
            VisualElement root,
            string logoAssetPath)
        {
            // Load and set Claude logo
            var logoImage = root.Q<Image>("welcome-logo-icon");
            if (logoImage != null)
            {
                var logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(logoAssetPath);
                if (logoTexture)
                {
                    logoImage.image = logoTexture;
                }
            }
        }

        private static void SetupLoginOverlayImages(VisualElement loginOverlay, string logoAssetPath)
        {
            if (loginOverlay == null) return;

            // Load Sidekick logo for login screens
            var logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(logoAssetPath);
            loginOverlay.Query<Image>(className: "sk-login-logo").ForEach(logoSprite =>
            {
                if (logoSprite != null && logoTexture)
                {
                    logoSprite.image = logoTexture;
                }
            });

            // Set placeholder text for OAuth code input
            var codeInput = loginOverlay.Q<TextField>("oauth-code-input");
            if (codeInput != null)
            {
                codeInput.textEdition.placeholder = "012345";
            }
        }
    }
}
