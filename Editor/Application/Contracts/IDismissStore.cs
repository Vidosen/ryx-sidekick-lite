// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Persists per-version update dismissals so a user is not re-notified
    /// about an update they have already seen and consciously skipped.
    /// </summary>
    internal interface IDismissStore
    {
        /// <summary>Returns <c>true</c> when the user has already dismissed the update for the given package + version pair.</summary>
        bool IsDismissed(string packageId, string latestVersion);

        /// <summary>Records a dismissal for the given package + version pair.</summary>
        void Dismiss(string packageId, string latestVersion);
    }
}
