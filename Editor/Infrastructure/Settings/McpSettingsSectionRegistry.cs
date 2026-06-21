// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Ordered registry of MCP settings sections. Mirrors CliProviderRegistry's permanent/transient split
    /// (so extension bootstraps survive ResetForTests without a domain reload) with two deltas:
    ///  (1) registration is REPLACE-BY-ID, not first-wins, so Sidekick Pro (B6) can override the Lite
    ///      section by reusing its Id deterministically regardless of [InitializeOnLoad] order;
    ///  (2) replace is IN-PLACE — an existing Id keeps its slot in registration order; and the permanent
    ///      baseline is ALSO replace-by-Id, so ResetForTests restores the latest (Pro) section, not the Lite one.
    /// </summary>
    internal static class McpSettingsSectionRegistry
    {
        private static readonly Dictionary<string, IMcpSettingsSection> _sections;
        private static readonly List<string> _order;            // registration order, stable across replace
        private static readonly List<IMcpSettingsSection> _permanent; // baseline restored by ResetForTests

        static McpSettingsSectionRegistry()
        {
            _sections = new Dictionary<string, IMcpSettingsSection>();
            _order = new List<string>();
            _permanent = new List<IMcpSettingsSection>();
            RegisterPermanent(new McpConfigSourceSection()); // Order 10
            RegisterPermanent(new McpServersSection());      // Order 20
        }

        /// <summary>Registers (or replaces in-place) a section by Id. Used by test fakes (NOT permanent).</summary>
        public static void Register(IMcpSettingsSection section)
        {
            if (section == null || string.IsNullOrEmpty(section.Id))
                return;
            if (!_sections.ContainsKey(section.Id))
                _order.Add(section.Id);   // new Id -> append slot
            _sections[section.Id] = section; // existing Id -> overwrite, slot preserved
        }

        /// <summary>
        /// Registers a section into the permanent baseline (survives ResetForTests) AND registers it.
        /// Permanent baseline is ALSO replace-by-Id, so a Pro section overriding a Lite section's Id
        /// becomes the restored baseline (not the Lite one). Extension bootstraps must use this.
        /// </summary>
        internal static void RegisterPermanent(IMcpSettingsSection section)
        {
            if (section == null || string.IsNullOrEmpty(section.Id))
                return;
            var existing = _permanent.FindIndex(s => s.Id == section.Id);
            if (existing >= 0) _permanent[existing] = section; // replace, NOT first-wins
            else _permanent.Add(section);
            Register(section);
        }

        /// <summary>Sections sorted by Order ascending; ties broken by registration order (stable).</summary>
        public static IReadOnlyList<IMcpSettingsSection> Sections =>
            _order.Select(id => _sections[id])
                  .OrderBy(s => s.Order)            // Enumerable.OrderBy is a stable sort -> ties keep _order
                  .ToList();

        /// <summary>Test-only: clears all sections and restores the permanent baseline. Tests that
        /// Register fakes MUST call this in teardown (static-registry leak hazard — same as CliProviderRegistry).
        /// NOTE: this does NOT undo <see cref="RegisterPermanent"/> — those mutate the baseline itself. A test
        /// that calls RegisterPermanent must snapshot/restore the baseline (see SnapshotPermanentForTests).</summary>
        internal static void ResetForTests()
        {
            _sections.Clear();
            _order.Clear();
            foreach (var s in _permanent)
                Register(s);
        }

        /// <summary>Test-only: snapshot the permanent baseline. A test that calls <see cref="RegisterPermanent"/>
        /// must capture this in SetUp and pass it to <see cref="RestorePermanentForTests"/> in TearDown — otherwise
        /// the permanent fake leaks into the live registry until the next domain reload.</summary>
        internal static IMcpSettingsSection[] SnapshotPermanentForTests() => _permanent.ToArray();

        /// <summary>Test-only: restore the permanent baseline from a snapshot, then rebuild the section list.</summary>
        internal static void RestorePermanentForTests(IMcpSettingsSection[] snapshot)
        {
            _permanent.Clear();
            if (snapshot != null)
                _permanent.AddRange(snapshot);
            ResetForTests();
        }
    }
}
