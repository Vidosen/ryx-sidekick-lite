// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Account;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Service contract for managing a Sidekick account session (ryx-sidekick.pro).
    /// </summary>
    internal interface ISidekickAccountService
    {
        /// <summary>Fired when the account sign-in state changes.</summary>
        event Action<SidekickAccountStatus> OnStatusChanged;

        /// <summary>Returns the current account status (reads from persisted settings).</summary>
        SidekickAccountStatus GetStatus();

        /// <summary>
        /// Starts the browser-based OAuth login flow.
        /// </summary>
        /// <param name="openBrowser">Callback that receives the bridge URL to open in a browser.</param>
        /// <returns>A result indicating success or failure after the flow completes.</returns>
        Task<SidekickAccountResult> StartLoginAsync(Action<string> openBrowser);

        /// <summary>Cancels any in-progress login flow.</summary>
        SidekickAccountResult CancelLogin();

        /// <summary>
        /// Handles a manually entered authorization code (format: "code" or "code#state").
        /// </summary>
        SidekickAccountResult HandleManualCode(string code);

        /// <summary>Uses the stored refresh token to obtain a new access token.</summary>
        Task<SidekickAccountResult> RefreshAsync();

        /// <summary>Clears all stored account credentials and session data.</summary>
        Task<bool> SignOutAsync();
    }
}
