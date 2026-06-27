// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// One staged daemon payload file: the on-disk name to materialize as plus its raw bytes (read from
    /// the imported <c>.bytes</c> TextAsset under <c>Editor/AgentHostPayload/</c>).
    /// </summary>
    internal readonly struct AgentHostPayloadFile
    {
        public AgentHostPayloadFile(string fileName, byte[] bytes)
        {
            FileName = fileName;
            Bytes = bytes;
        }

        /// <summary>Materialized file name (e.g. <c>SidekickAgentHost.dll</c>) — the <c>.bytes</c> suffix is already stripped.</summary>
        public string FileName { get; }

        /// <summary>Raw file content.</summary>
        public byte[] Bytes { get; }
    }

    /// <summary>
    /// Pure (Unity-free) materialization logic: writes the staged daemon payload files into a per-user,
    /// per-project <c>bin/</c> directory and re-extracts only when the staged VERSION differs from the
    /// copy already on disk (the version stamp written alongside the binaries).
    ///
    /// <para>
    /// Kept free of <c>UnityEditor</c>/<c>AssetDatabase</c> so it is unit-testable: the caller (the
    /// real <see cref="AgentHostConnector"/>) loads the <c>.bytes</c> TextAssets and feeds the bytes in;
    /// tests feed synthetic bytes and assert the bytes→disk + re-extract-on-version-change behavior.
    /// </para>
    /// </summary>
    internal static class AgentHostPayloadStore
    {
        internal const string VersionStampFileName = "version.txt";

        /// <summary>
        /// Ensure the payload is materialized under <paramref name="binDir"/>. Writes every file in
        /// <paramref name="files"/> and a <see cref="VersionStampFileName"/> stamp, but only when the
        /// existing stamp differs from <paramref name="stagedVersion"/> or a required file is missing.
        /// Returns the absolute path of the materialized primary dll (or <c>null</c> if it is absent
        /// from <paramref name="files"/>).
        /// </summary>
        /// <param name="binDir">Target directory (created if needed).</param>
        /// <param name="stagedVersion">Version of the staged payload (from the shipped <c>VERSION.txt</c>).</param>
        /// <param name="files">Files to materialize.</param>
        /// <param name="primaryDllName">Name of the dll to return the path of (default the daemon dll).</param>
        public static string EnsureMaterialized(
            string binDir,
            string stagedVersion,
            IReadOnlyList<AgentHostPayloadFile> files,
            string primaryDllName = AgentHostPayloadNames.DaemonDll)
        {
            if (string.IsNullOrEmpty(binDir))
                throw new ArgumentException("binDir is required", nameof(binDir));
            if (files == null || files.Count == 0)
                throw new ArgumentException("at least one payload file is required", nameof(files));

            Directory.CreateDirectory(binDir);

            if (IsUpToDate(binDir, stagedVersion, files))
            {
                return PrimaryDllPath(binDir, files, primaryDllName);
            }

            foreach (var file in files)
            {
                if (string.IsNullOrEmpty(file.FileName) || file.Bytes == null)
                    continue;
                var dest = Path.Combine(binDir, file.FileName);
                WriteAtomic(dest, file.Bytes);
            }

            // Stamp LAST so a crash mid-extraction leaves a stale/missing stamp ⇒ re-extract next time.
            WriteAtomic(Path.Combine(binDir, VersionStampFileName),
                System.Text.Encoding.UTF8.GetBytes(stagedVersion ?? string.Empty));

            return PrimaryDllPath(binDir, files, primaryDllName);
        }

        /// <summary>
        /// True when the materialized copy is current: the version stamp equals <paramref name="stagedVersion"/>
        /// AND every payload file already exists on disk.
        /// </summary>
        public static bool IsUpToDate(string binDir, string stagedVersion, IReadOnlyList<AgentHostPayloadFile> files)
        {
            var stampPath = Path.Combine(binDir, VersionStampFileName);
            if (!File.Exists(stampPath))
                return false;

            string materializedVersion;
            try { materializedVersion = File.ReadAllText(stampPath).Trim(); }
            catch { return false; }

            if (!string.Equals(materializedVersion, (stagedVersion ?? string.Empty).Trim(), StringComparison.Ordinal))
                return false;

            foreach (var file in files)
            {
                if (string.IsNullOrEmpty(file.FileName))
                    continue;
                if (!File.Exists(Path.Combine(binDir, file.FileName)))
                    return false;
            }

            return true;
        }

        private static string PrimaryDllPath(string binDir, IReadOnlyList<AgentHostPayloadFile> files, string primaryDllName)
        {
            foreach (var file in files)
            {
                if (string.Equals(file.FileName, primaryDllName, StringComparison.Ordinal))
                    return Path.Combine(binDir, primaryDllName);
            }
            return null;
        }

        private static void WriteAtomic(string dest, byte[] bytes)
        {
            // Write to a temp sibling then move into place so a concurrent reader never sees a half-written
            // file. File.Move with overwrite is atomic on the same volume (which a temp sibling guarantees).
            var tmp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(dest))
                    File.Delete(dest);
                File.Move(tmp, dest);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Canonical materialized file names for the daemon payload (the <c>.bytes</c> suffix stripped).
    /// </summary>
    internal static class AgentHostPayloadNames
    {
        public const string DaemonDll = "SidekickAgentHost.dll";
        public const string RuntimeConfig = "SidekickAgentHost.runtimeconfig.json";
        public const string Deps = "SidekickAgentHost.deps.json";
    }
}
