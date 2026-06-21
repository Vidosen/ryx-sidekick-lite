// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Updates;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Updates
{
    internal sealed class CheckForUpdatesQuery
    {
        private const string LiteId = "com.ryxinteractive.sidekick";
        private const string ProId = "com.ryxinteractive.sidekick.pro";

        private readonly IInstalledPackageVersions _versions;
        private readonly IProPresence _proPresence;

        public CheckForUpdatesQuery(IInstalledPackageVersions versions, IProPresence proPresence)
        { _versions = versions; _proPresence = proPresence; }

        public IReadOnlyList<UpdateAvailability> Check(ReleasesInfo releases)
        {
            var result = new List<UpdateAvailability>();
            if (releases == null) return result;

            Evaluate(result, LiteId, releases.Lite);
            if (_proPresence != null && _proPresence.IsInstalled)
                Evaluate(result, ProId, releases.Pro);

            return result;
        }

        private void Evaluate(List<UpdateAvailability> result, string packageId, ReleaseInfo release)
        {
            if (release == null || string.IsNullOrWhiteSpace(release.Latest)) return;
            var installed = _versions?.GetVersion(packageId);
            if (string.IsNullOrEmpty(installed)) return;                 // unknown => suppress
            if (!SemVer.TryCompare(release.Latest, installed, out var cmp)) return; // unparseable => suppress
            result.Add(new UpdateAvailability
            {
                PackageId = packageId,
                InstalledVersion = installed,
                LatestVersion = release.Latest,
                HasUpdate = cmp > 0,
                Url = release.Url,
                ChangelogUrl = release.ChangelogUrl
            });
        }
    }
}
