// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Domain.Updates
{
    internal static class SemVer
    {
        /// <summary>
        /// Parses a version string of the form MAJOR[.MINOR[.PATCH]][-prerelease].
        /// Missing minor/patch default to 0. Returns false if the numeric core is unparseable.
        /// </summary>
        public static bool TryParse(string s, out int major, out int minor, out int patch, out string pre)
        {
            major = 0;
            minor = 0;
            patch = 0;
            pre = string.Empty;

            if (string.IsNullOrEmpty(s))
                return false;

            // Split off pre-release suffix on first '-'
            string numeric = s;
            int dashIdx = s.IndexOf('-');
            if (dashIdx >= 0)
            {
                pre = s.Substring(dashIdx + 1);
                numeric = s.Substring(0, dashIdx);
            }

            string[] parts = numeric.Split('.');
            if (parts.Length < 1 || parts.Length > 3)
                return false;

            if (!int.TryParse(parts[0], out major) || major < 0)
                return false;

            if (parts.Length >= 2)
            {
                if (!int.TryParse(parts[1], out minor) || minor < 0)
                    return false;
            }

            if (parts.Length >= 3)
            {
                if (!int.TryParse(parts[2], out patch) || patch < 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Returns false if either version string is unparseable.
        /// Otherwise sets result to -1, 0, or 1 following semver §11 precedence.
        /// </summary>
        public static bool TryCompare(string a, string b, out int result)
        {
            result = 0;

            if (!TryParse(a, out int aMaj, out int aMin, out int aPat, out string aPre))
                return false;
            if (!TryParse(b, out int bMaj, out int bMin, out int bPat, out string bPre))
                return false;

            // Compare numeric components
            int cmp = aMaj.CompareTo(bMaj);
            if (cmp != 0) { result = Normalise(cmp); return true; }

            cmp = aMin.CompareTo(bMin);
            if (cmp != 0) { result = Normalise(cmp); return true; }

            cmp = aPat.CompareTo(bPat);
            if (cmp != 0) { result = Normalise(cmp); return true; }

            // Pre-release: release (empty pre) > any pre-release
            bool aIsRelease = string.IsNullOrEmpty(aPre);
            bool bIsRelease = string.IsNullOrEmpty(bPre);

            if (aIsRelease && bIsRelease) { result = 0; return true; }
            if (aIsRelease && !bIsRelease) { result = 1; return true; }
            if (!aIsRelease && bIsRelease) { result = -1; return true; }

            // Both have pre-release — compare ordinally
            result = Normalise(string.CompareOrdinal(aPre, bPre));
            return true;
        }

        /// <summary>
        /// Compares two version strings. Returns the comparison result (-1/0/1).
        /// Returns 0 if either string is unparseable.
        /// </summary>
        public static int Compare(string a, string b)
        {
            TryCompare(a, b, out int result);
            return result;
        }

        private static int Normalise(int cmp)
        {
            if (cmp < 0) return -1;
            if (cmp > 0) return 1;
            return 0;
        }
    }
}
