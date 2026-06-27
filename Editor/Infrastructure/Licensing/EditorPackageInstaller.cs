// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Licensing
{
    internal sealed class EditorPackageInstaller : IPackageInstaller
    {
        public void StageUpdate(string sku, string version, string artifactPath)
        {
            // The downloaded artifact IS the self-contained two-stage installer .unitypackage:
            // it bundles the [InitializeOnLoad] reconciler (RyxSidekickInstaller) + installer.json
            // + the inner package payload. Importing it drops those into Assets/Ryx Sidekick/Installer/;
            // the reconciler then runs backup→import→finalize→self-clean on the resulting domain reload
            // (and a `.devproject` marker keeps it inert in the dev repo, by design).
            //
            // We deliberately do NOT copy the file or pre-write a manifest ourselves — the artifact is
            // authoritative — and we do NOT force a reload: importing scripts triggers its own
            // compile+reload, and a forced RequestScriptReload here would race the importer and could
            // drop the reconciler's [InitializeOnLoad] activation.
            Debug.Log($"[Ryx Sidekick] Importing {sku} {version} installer artifact…");
            AssetDatabase.ImportPackage(artifactPath, interactive: false);
        }
    }
}
