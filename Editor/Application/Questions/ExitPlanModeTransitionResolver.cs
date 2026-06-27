// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal static class ExitPlanModeTransitionResolver
    {
        /// <summary>
        /// Resolves the collaboration/permission mode transition applied when the user answers an
        /// ExitPlanMode prompt. The permission mode is a sticky user choice that is preserved across
        /// the plan→execution transition: accepting the plan to proceed automatically keeps whatever
        /// mode was active (auto / acceptEdits / bypassPermissions / default). The single case that
        /// resets it is the explicit "accept with manual permission validation" choice, which returns
        /// to <c>default</c> (ask before edits).
        /// </summary>
        public static AskUserQuestionModeTransition Create(
            bool autoApprove,
            string permissionMode,
            IProviderUiMetadata activeProvider)
        {
            var collaborationMode = SidekickAppConstants.CollaborationModes.Default;

            string targetPermissionMode;
            if (autoApprove)
            {
                // Preserve the current (pre-plan) permission mode. Normalize against the default
                // collaboration mode so a provider that doesn't expose the stored value falls back
                // gracefully; the real Claude provider lists every mode, so it round-trips unchanged.
                var preserved = activeProvider != null
                    ? activeProvider.NormalizeModeSelection(collaborationMode, permissionMode).PermissionMode
                    : permissionMode;

                targetPermissionMode = string.IsNullOrEmpty(preserved)
                    ? SidekickAppConstants.PermissionModes.Default
                    : preserved;
            }
            else
            {
                // "Accept plan with manual permission validation" → reset to default.
                targetPermissionMode = SidekickAppConstants.PermissionModes.Default;
            }

            return new AskUserQuestionModeTransition
            {
                CollaborationMode = collaborationMode,
                PermissionMode = targetPermissionMode
            };
        }
    }
}
