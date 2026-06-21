// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Normalized provider mode pair used across settings, UI, and runtime mapping.
    /// </summary>
    internal readonly struct ProviderModeSelection
    {
        public string CollaborationMode { get; }
        public string PermissionMode { get; }

        public ProviderModeSelection(string collaborationMode, string permissionMode)
        {
            CollaborationMode = collaborationMode;
            PermissionMode = permissionMode;
        }
    }
}
