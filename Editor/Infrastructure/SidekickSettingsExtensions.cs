// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Bridge between the Editor-bound <see cref="SidekickSettings"/> ScriptableSingleton
    /// and the layer-agnostic <see cref="CliInvocationSettings"/> DTO consumed by
    /// provider contract methods.
    /// </summary>
    internal static class SidekickSettingsExtensions
    {
        /// <summary>
        /// Projects the user's current settings into a minimal DTO so provider runtime
        /// clients can consume them without taking a dependency on UnityEditor.
        /// </summary>
        public static CliInvocationSettings ToInvocationSettings(this SidekickSettings settings)
        {
            if (settings == null)
            {
                return new CliInvocationSettings(null, null, null, null);
            }

            return new CliInvocationSettings(
                workingDirectory: settings.WorkingDirectory,
                model: settings.Model,
                collaborationMode: settings.CollaborationMode,
                permissionMode: settings.PermissionMode,
                reasoningEffort: settings.ReasoningEffort);
        }
    }
}
