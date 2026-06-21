// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Updates;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Updates;
using Unity.AppUI.MVVM;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    [ObservableObject]
    internal partial class UpdateNotificationViewModel : IDisposable
    {
        private readonly CheckForUpdatesQuery _check;
        private readonly IRemoteConfigSource _config;
        private readonly IExternalUrlOpener _opener;
        private readonly IUpdateNotifier _notifier;
        private readonly IDismissStore _dismissStore;

        public UpdateNotificationViewModel(
            CheckForUpdatesQuery check,
            IRemoteConfigSource config,
            IExternalUrlOpener opener,
            IUpdateNotifier notifier,
            IDismissStore dismissStore)
        {
            _check = check;
            _config = config;
            _opener = opener;
            _notifier = notifier;
            _dismissStore = dismissStore;
        }

        /// <summary>
        /// Evaluates available updates from the current remote config snapshot and surfaces
        /// any that have not yet been dismissed by the user.
        /// </summary>
        public void Evaluate()
        {
            var releases = _config.Current?.Releases;
            var updates = _check.Check(releases);

            var filtered = new List<UpdateAvailability>();
            foreach (var u in updates)
            {
                if (u.HasUpdate && !_dismissStore.IsDismissed(u.PackageId, u.LatestVersion))
                    filtered.Add(u);
            }

            if (filtered.Count == 0) return;

            _notifier.ShowUpdates(filtered, OnUpdate, OnWhatsNew);
        }

        /// <summary>
        /// Records that the user has dismissed the update notification for a specific
        /// package + version pair. Subsequent <see cref="Evaluate"/> calls will skip it.
        /// </summary>
        public void Dismiss(string packageId, string latestVersion) =>
            _dismissStore.Dismiss(packageId, latestVersion);

        private void OnUpdate(UpdateAvailability u)
        {
            if (!string.IsNullOrWhiteSpace(u.Url))
                _opener.Open(u.Url);
        }

        private void OnWhatsNew(UpdateAvailability u)
        {
            if (!string.IsNullOrWhiteSpace(u.ChangelogUrl))
                _opener.Open(u.ChangelogUrl);
        }

        public void Dispose()
        {
            // No subscriptions to clean up in this VM; kept for lifecycle symmetry with other VMs.
        }
    }
}
