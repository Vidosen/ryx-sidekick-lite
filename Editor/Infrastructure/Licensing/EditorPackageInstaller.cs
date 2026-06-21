// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using UnityEditor;
using UnityEngine;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Licensing
{
    internal sealed class EditorPackageInstaller : IPackageInstaller
    {
        // Mirror Assets/Ryx Sidekick/Installer constants (that assembly is not referenceable from here).
        private const string InstallerRoot = "Assets/Ryx Sidekick/Installer";
        private const string PayloadFileName = "payload.unitypackage";
        private const string ManifestFileName = "installer.json";

        [System.Serializable]
        private sealed class ManifestDto
        {
            public int schemaVersion = 1;
            public string sku;
            public string version;
            public string[] packages;
            public string payload = PayloadFileName;
        }

        public void StageUpdate(string sku, string version, string[] packages, string payloadSourcePath)
        {
            Directory.CreateDirectory(InstallerRoot);

            var payloadDest = Path.Combine(InstallerRoot, PayloadFileName);
            File.Copy(payloadSourcePath, payloadDest, overwrite: true);

            var manifest = new ManifestDto { sku = sku, version = version, packages = packages };
            File.WriteAllText(Path.Combine(InstallerRoot, ManifestFileName), JsonUtility.ToJson(manifest, true));

            AssetDatabase.Refresh();
            // The two-stage installer (RyxSidekickInstaller, [InitializeOnLoad] in the Assets
            // installer assembly) detects the manifest + payload and runs backup→import→finalize
            // on the next domain reload. Request a reload so it activates without manual action.
            // In the dev repo a `.devproject` marker keeps that installer inert (by design).
            EditorUtility.RequestScriptReload();
        }
    }
}
