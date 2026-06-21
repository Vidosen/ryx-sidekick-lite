// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal static class ExitPlanModeTransitionResolver
    {
        public static AskUserQuestionModeTransition Create(
            bool autoApprove,
            string permissionMode,
            IProviderUiMetadata activeProvider)
        {
            var collaborationMode = SidekickAppConstants.CollaborationModes.Default;

            if (activeProvider != null)
            {
                var normalizedModes = activeProvider.NormalizeModeSelection(collaborationMode, permissionMode);
                var defaultPermissionMode = normalizedModes.PermissionMode;
                var autoPermissionMode = activeProvider
                    .GetPermissionModes(collaborationMode)
                    .Select(mode => mode.Value)
                    .FirstOrDefault(activeProvider.IsAutoApprovePermissionMode);

                permissionMode = autoApprove
                    ? autoPermissionMode ?? defaultPermissionMode
                    : defaultPermissionMode;
            }

            return new AskUserQuestionModeTransition
            {
                CollaborationMode = collaborationMode,
                PermissionMode = permissionMode
            };
        }
    }
}
