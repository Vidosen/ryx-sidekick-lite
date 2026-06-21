// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEditor.PackageManager;

namespace Ryx.Sidekick.Editor.Infrastructure.Updates
{
    internal sealed class PackageManagerInstalledVersions : IInstalledPackageVersions
    {
        public string GetVersion(string packageName)
        {
            try
            {
                if (string.IsNullOrEmpty(packageName)) return null;
                var info = PackageInfo.FindForPackageName(packageName);
                return info?.version;
            }
            catch { return null; }
        }
    }
}
