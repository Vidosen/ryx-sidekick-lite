// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Presentation.Constants
{
    /// <summary>
    /// Centralized brand, UI paths, and toolbar identifiers for Ryx Sidekick.
    /// Single source of truth for rebrand and UI asset references.
    /// </summary>
    internal static class SidekickUiConstants
    {
        public const string BrandName = "Ryx Sidekick";
        public const string BrandNameUpper = "RYX SIDEKICK";

        public const string PackageId = "com.ryxinteractive.sidekick";
        public const string PackageRoot = "Packages/" + PackageId;

        public const string UiFolder = PackageRoot + "/Editor/UI/";
        public const string MainWindowUxml = UiFolder + "SidekickWindow.uxml";
        public const string MainWindowUss = UiFolder + "SidekickWindow.uss";

        public const string StylesFolder = UiFolder + "Styles/";
        public const string MainWindowMarkdownUss = StylesFolder + "SidekickWindow.Markdown.uss";
        public const string MainWindowInlineToolsUss = StylesFolder + "SidekickWindow.InlineTools.uss";
        public const string MainWindowMiscUss = StylesFolder + "SidekickWindow.Misc.uss";
        public const string MainWindowAuthUss = StylesFolder + "SidekickWindow.Auth.uss";
        public const string MainWindowAskUserQuestionUss = StylesFolder + "SidekickWindow.AskUserQuestion.uss";
        public const string MainWindowPermissionUss = StylesFolder + "SidekickWindow.Permission.uss";
        public const string MainWindowContextUss = StylesFolder + "SidekickWindow.Context.uss";
        public const string MainWindowCommandPaletteUss = StylesFolder + "SidekickWindow.CommandPalette.uss";
        public const string MainWindowOnboardingUss = StylesFolder + "SidekickWindow.Onboarding.uss";
        public const string MainWindowConfirmDialogUss = StylesFolder + "SidekickWindow.ConfirmDialog.uss";

        /// <summary>
        /// Partial stylesheets cascaded onto <c>Panel.popupContainer</c>,
        /// <c>notificationContainer</c>, and <c>tooltipContainer</c> by <c>SidekickAppPanel</c>
        /// so that off-tree App UI Modal/Toast/Popover content inherits the correct styles.
        /// Permission and AskUserQuestion USS are intentionally excluded: those overlays are now
        /// mounted inline inside <c>contentContainer</c> and already inherit their styles from
        /// the &lt;Style src="..."/&gt; block in <c>SidekickWindow.uxml</c>.
        /// </summary>
        public static readonly string[] MainWindowPartialUssPaths =
        {
            MainWindowMarkdownUss,
            MainWindowInlineToolsUss,
            MainWindowMiscUss,
            MainWindowAuthUss,
            MainWindowContextUss,
            MainWindowCommandPaletteUss,
            MainWindowOnboardingUss,
            MainWindowConfirmDialogUss,
        };
        public const string LoginOverlayUxmlPath = UiFolder + "Templates/LoginOverlay.uxml";
        public const string AccountOverlayUxmlPath = UiFolder + "Templates/SidekickAccountOverlay.uxml";
        public const string AskUserQuestionOverlayUxmlPath = UiFolder + "Templates/AskUserQuestionOverlay.uxml";
        public const string PermissionOverlayUxmlPath = UiFolder + "Templates/PermissionOverlay.uxml";
        public const string OnboardingOverlayUxmlPath = UiFolder + "Templates/OnboardingOverlay.uxml";
        public const string ProviderPopoverContentUxmlPath = UiFolder + "Templates/ProviderPopoverContent.uxml";
        public const string ModelPopoverContentUxmlPath = UiFolder + "Templates/ModelPopoverContent.uxml";
        public const string AttachmentMenuContentUxmlPath = UiFolder + "Templates/AttachmentMenuContent.uxml";
        public const string MessageBubbleTemplatePath = UiFolder + "Templates/MessageBubble.uxml";
        public const string ToolCallTemplatePath = UiFolder + "Templates/ToolCall.uxml";

        public const string AssetsPath = PackageRoot + "/Assets/";
        public const string LogoAssetPath = AssetsPath + "sidekick-logo.png";
        public const string ToolbarIconPath = AssetsPath + "toolbar-icon.png";
        public const string CopyIconPath = AssetsPath + "copy-icon.png";
        public const string ChevronDownIconPath = AssetsPath + "chevron-down.png";
        public const string ChevronUpIconPath = AssetsPath + "chevron-up.png";
        public const string RobotoMonoFontTtfPath = AssetsPath + "RobotoMono-wght.ttf";

        public const string PackageJsonPath = PackageRoot + "/package.json";
        public const string DocumentationIndexPath = PackageRoot + "/Documentation~/index.html";
        public const string ChangelogPath = PackageRoot + "/CHANGELOG.md";
        public const string DocumentationLogoRelativePath = "../Assets/sidekick-logo.png";

        /// <summary>Toolbar button element name (pre-Unity 6.0.3).</summary>
        public const string ToolbarButtonName = "sidekick-toolbar-button";

        /// <summary>Main toolbar overlay element ID (Unity 6.0.3+).</summary>
        public const string ToolbarOverlayElementId = "Sidekick/Open Chat";

        /// <summary>Temp file prefix for clipboard image attachments.</summary>
        public const string ClipboardTempFilePrefix = "sidekick-clipboard";
    }
}
