// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Minimal, immutable projection of user settings that providers actually need at turn-start time.
    /// Deliberately decoupled from the concrete SidekickSettings ScriptableSingleton so that
    /// Domain/provider contracts do not pull in UnityEditor types.
    /// </summary>
    /// <remarks>
    /// New fields should only be added here when a concrete provider impl needs them in a contract
    /// method body — do not mirror the entire SidekickSettings shape.
    /// </remarks>
    internal sealed class CliInvocationSettings
    {
        public CliInvocationSettings(
            string workingDirectory,
            string model,
            string collaborationMode,
            string permissionMode,
            string reasoningEffort = null)
        {
            WorkingDirectory = workingDirectory;
            Model = model;
            CollaborationMode = collaborationMode;
            PermissionMode = permissionMode;
            ReasoningEffort = reasoningEffort;
        }

        /// <summary>Project / repository root used as cwd for the CLI process.</summary>
        public string WorkingDirectory { get; }

        /// <summary>Selected model preset id (e.g. "sonnet", "opus", "haiku").</summary>
        public string Model { get; }

        /// <summary>Active collaboration mode (e.g. "default", "plan").</summary>
        public string CollaborationMode { get; }

        /// <summary>Active permission mode (e.g. "default", "bypassPermissions", "fullAuto", "danger").</summary>
        public string PermissionMode { get; }

        /// <summary>Provider-specific reasoning effort value, when supported.</summary>
        public string ReasoningEffort { get; }
    }
}
