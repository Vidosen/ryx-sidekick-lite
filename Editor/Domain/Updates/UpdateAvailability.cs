// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Domain.Updates
{
    internal sealed class UpdateAvailability
    {
        public string PackageId;
        public string InstalledVersion;
        public string LatestVersion;
        public bool HasUpdate;
        public string Url;
        public string ChangelogUrl;
    }
}
