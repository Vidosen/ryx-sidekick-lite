// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Permissions
{
    internal sealed class AutoAcceptPermissionPolicy
    {
        public bool ShouldAutoAccept(ISettingsStore settingsStore, IProviderUiMetadata activeProvider)
        {
            if (settingsStore == null || activeProvider == null)
            {
                return false;
            }

            return activeProvider.IsAutoApprovePermissionMode(settingsStore.PermissionMode);
        }
    }
}
