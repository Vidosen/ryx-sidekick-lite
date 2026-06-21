// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace Ryx.Sidekick.Editor.Domain.Updates
{
    /// <summary>
    /// A single entry from the optional <c>versions[]</c> array in config.json.
    /// </summary>
    internal sealed class ReleaseVersionInfo
    {
        /// <summary>Semver version string, e.g. "1.3.5".</summary>
        public string Version { get; set; }
        /// <summary>ISO-8601 release date string, e.g. "2026-06-15". Parse to unix seconds as needed.</summary>
        public string ReleaseDate { get; set; }
        /// <summary>Edition year label, e.g. 2026.</summary>
        public int EditionYear { get; set; }
    }

    internal sealed class ReleaseInfo
    {
        public string Latest { get; set; }
        public string Url { get; set; }
        public string ChangelogUrl { get; set; }
        /// <summary>
        /// Optional per-version metadata array. Present in v2+ config.json; absent in older
        /// baked configs — callers must treat null/empty as "no version list available".
        /// </summary>
        public List<ReleaseVersionInfo> Versions { get; set; }
    }
}
