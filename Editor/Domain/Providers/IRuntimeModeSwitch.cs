// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;

namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Optional capability implemented by persistent session runtime clients that can switch the
    /// permission mode / model on a live session without restarting the process (Claude stream-json
    /// control_request <c>set_permission_mode</c> / <c>set_model</c>).
    ///
    /// <para>
    /// This is deliberately NOT part of <see cref="ISessionRuntimeClient"/> so that providers without
    /// live-switching support (Cursor / Codex) are not forced to implement it. Consumers probe with
    /// <c>client is IRuntimeModeSwitch</c> and fall back to a relaunch when absent.
    /// </para>
    /// </summary>
    internal interface IRuntimeModeSwitch
    {
        /// <summary>
        /// Switches the permission mode on the live session. No-op when the session is not running
        /// (the mode is already persisted and will apply on the next start).
        /// </summary>
        Task SetPermissionModeAsync(string mode);

        /// <summary>
        /// Switches the model on the live session. No-op when the session is not running.
        /// </summary>
        Task SetModelAsync(string model);
    }
}
