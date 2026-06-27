// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal interface IPackageInstaller
    {
        /// <summary>
        /// Import a downloaded self-contained installer artifact (the two-stage installer
        /// .unitypackage: the <c>[InitializeOnLoad]</c> reconciler + its <c>installer.json</c> +
        /// the inner payload). Importing it extracts the reconciler into the project, which then
        /// runs backup→import→finalize→self-clean on the ensuing domain reload. The artifact is
        /// authoritative — do NOT pre-write a manifest or force a reload here.
        /// <paramref name="artifactPath"/> is the local .unitypackage to import;
        /// <paramref name="sku"/>/<paramref name="version"/> are for diagnostics/logging only.
        /// </summary>
        void StageUpdate(string sku, string version, string artifactPath);
    }
}
