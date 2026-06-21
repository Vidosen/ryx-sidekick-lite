// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal interface IInstalledPackageVersions
    {
        string GetVersion(string packageName); // null when unknown/not installed
    }
}
