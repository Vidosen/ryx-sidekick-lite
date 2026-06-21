// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using Ryx.Sidekick.Editor.Domain.Updates;

namespace Ryx.Sidekick.Editor.UseCases.Licensing
{
    /// <summary>
    /// Input DTO: a single release entry from the remote config versions[] array.
    /// </summary>
    internal readonly struct ReleaseVersionEntry
    {
        public readonly string Version;
        public readonly long ReleaseDateUnix;
        public readonly int EditionYear;

        public ReleaseVersionEntry(string version, long releaseDateUnix, int editionYear)
        {
            Version = version;
            ReleaseDateUnix = releaseDateUnix;
            EditionYear = editionYear;
        }
    }

    /// <summary>
    /// Result of <see cref="EntitledReleaseResolver.Resolve"/>.
    /// </summary>
    internal readonly struct EntitledReleaseResult
    {
        /// <summary>
        /// The newest release whose <c>ReleaseDateUnix</c> falls within the support window
        /// (<c>releaseDate &lt;= supportUntil</c>). <c>null</c> when no eligible release exists.
        /// </summary>
        public readonly string Version;

        /// <summary>
        /// <c>true</c> when at least one release has a <c>ReleaseDateUnix</c> beyond the support
        /// window — i.e. there is a newer release the user cannot yet download. Use this to show
        /// a "renew to get newer updates" CTA.
        /// </summary>
        public readonly bool HasNewerOutOfWindow;

        public EntitledReleaseResult(string version, bool hasNewerOutOfWindow)
        {
            Version = version;
            HasNewerOutOfWindow = hasNewerOutOfWindow;
        }
    }

    /// <summary>
    /// Pure, stateless resolver that determines which release a user is entitled to download
    /// given their support window end timestamp (<c>supportUntil</c>, unix seconds).
    ///
    /// Rules:
    /// - "In-window" = <c>ReleaseDateUnix &lt;= supportUntil</c>.
    /// - Among in-window releases, pick the one with the greatest <c>ReleaseDateUnix</c>
    ///   (most recent release before or on the window boundary).
    ///   Tie-break: string ordinal comparison of <c>Version</c> (descending), so "1.3.5" beats
    ///   "1.3.4" when two releases share the same timestamp.  This is intentionally simple; the
    ///   canonical authority is the server — this resolver only drives the local "which version
    ///   should we offer to download" UI hint.
    /// - <c>HasNewerOutOfWindow = true</c> if any release has <c>ReleaseDateUnix &gt; supportUntil</c>.
    ///
    /// Pure Application-layer logic — no UI or Editor dependencies; safe for the layering guard.
    /// </summary>
    internal static class EntitledReleaseResolver
    {
        public static EntitledReleaseResult Resolve(
            IReadOnlyList<ReleaseVersionEntry> releases,
            long supportUntil)
        {
            if (releases == null || releases.Count == 0)
                return new EntitledReleaseResult(null, false);

            string bestVersion = null;
            long bestDate = long.MinValue;
            bool hasNewerOutOfWindow = false;

            foreach (var r in releases)
            {
                if (r.ReleaseDateUnix <= supportUntil)
                {
                    // Pick newest in-window release: first by date, then by version string (descending).
                    if (r.ReleaseDateUnix > bestDate ||
                        (r.ReleaseDateUnix == bestDate &&
                         string.CompareOrdinal(r.Version, bestVersion) > 0))
                    {
                        bestDate = r.ReleaseDateUnix;
                        bestVersion = r.Version;
                    }
                }
                else
                {
                    hasNewerOutOfWindow = true;
                }
            }

            return new EntitledReleaseResult(bestVersion, hasNewerOutOfWindow);
        }

        /// <summary>
        /// Convenience overload that accepts the remote-config <see cref="ReleaseVersionInfo"/>
        /// shape (ISO-8601 date strings) directly. Each entry is mapped to a
        /// <see cref="ReleaseVersionEntry"/> by parsing <c>ReleaseDate</c> to unix seconds, then
        /// delegated to <see cref="Resolve(IReadOnlyList{ReleaseVersionEntry}, long)"/>.
        ///
        /// Entries with an empty <c>Version</c> or an unparseable <c>ReleaseDate</c> are skipped
        /// entirely — they are neither selected nor counted toward <c>HasNewerOutOfWindow</c>, so a
        /// malformed date can never light up a false "renew" hint.
        /// A null/empty list yields <c>(null, false)</c>.
        /// </summary>
        public static EntitledReleaseResult Resolve(
            IReadOnlyList<ReleaseVersionInfo> versions,
            long supportUntil)
        {
            if (versions == null || versions.Count == 0)
                return new EntitledReleaseResult(null, false);

            var entries = new List<ReleaseVersionEntry>(versions.Count);
            foreach (var v in versions)
            {
                if (v == null || string.IsNullOrEmpty(v.Version))
                    continue;
                if (!TryParseIsoToUnix(v.ReleaseDate, out var unix))
                    continue;
                entries.Add(new ReleaseVersionEntry(v.Version, unix, v.EditionYear));
            }

            return Resolve(entries, supportUntil);
        }

        /// <summary>
        /// Parses an ISO-8601 date string to unix seconds. Date-only values ("2026-06-15") are
        /// treated as UTC midnight; values with an explicit offset are normalized to UTC.
        /// Returns <c>false</c> for null/empty/unparseable input.
        /// </summary>
        private static bool TryParseIsoToUnix(string iso, out long unix)
        {
            unix = 0;
            if (string.IsNullOrWhiteSpace(iso))
                return false;
            if (DateTimeOffset.TryParse(
                    iso,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dto))
            {
                unix = dto.ToUnixTimeSeconds();
                return true;
            }
            return false;
        }
    }
}
