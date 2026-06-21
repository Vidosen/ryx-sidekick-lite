// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Resolves the context-window size (max input tokens) for a model id.
    ///
    /// <para>
    /// The real per-model window is only ever delivered reactively by the provider CLIs, inside a
    /// token-usage / result event (Claude: <c>result.modelUsage[id].contextWindow</c>). There is no
    /// up-front "models with limits" capability in any CLI — neither the Claude <c>initialize</c>
    /// model list, the <c>system init</c> event, nor the Codex <c>model/list</c> RPC carry a window.
    /// </para>
    /// <para>
    /// So we harvest the real value via <see cref="Record"/> whenever a turn reports it, persist it
    /// per model id, and reuse it for history / cold-start lookups. A small id heuristic is used only
    /// until the real value for a given model has been observed at least once.
    /// </para>
    /// </summary>
    internal static class ModelContextWindowRegistry
    {
        private const string PrefsKey = "Sidekick_ObservedContextWindows";
        private const int DefaultContextWindow = 200_000;
        private const int OneMillionContextWindow = 1_000_000;

        private static readonly object Gate = new object();

        // Lazily hydrated from EditorPrefs on first access; null means "not yet hydrated".
        private static Dictionary<string, int> _observed;

        /// <summary>
        /// Returns the best-known context window for <paramref name="modelId"/>: the real observed
        /// value if we have ever seen it for this model, otherwise a cold-start heuristic.
        /// </summary>
        public static int Resolve(string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                return DefaultContextWindow;

            lock (Gate)
            {
                EnsureHydrated();
                if (_observed.TryGetValue(modelId, out var window) && window > 0)
                    return window;
            }

            return Heuristic(modelId);
        }

        /// <summary>
        /// Records a real context-window value observed in a CLI usage/result event.
        /// No-op for empty ids or non-positive values; persists only when the value is new or changed.
        /// </summary>
        public static void Record(string modelId, int contextWindow)
        {
            if (string.IsNullOrEmpty(modelId) || contextWindow <= 0)
                return;

            lock (Gate)
            {
                EnsureHydrated();
                if (_observed.TryGetValue(modelId, out var existing) && existing == contextWindow)
                    return;

                _observed[modelId] = contextWindow;
                Persist();
            }
        }

        /// <summary>
        /// Test hook: clears the in-memory cache AND the persisted EditorPrefs entry.
        /// </summary>
        internal static void ResetForTests()
        {
            lock (Gate)
            {
                _observed = new Dictionary<string, int>(StringComparer.Ordinal);
                EditorPrefs.DeleteKey(PrefsKey);
            }
        }

        /// <summary>
        /// Test hook: drops the in-memory cache WITHOUT touching EditorPrefs, so the next access
        /// re-hydrates from persistence (simulates a domain reload).
        /// </summary>
        internal static void DropInMemoryCacheForTests()
        {
            lock (Gate)
            {
                _observed = null;
            }
        }

        // 1M-context variants carry a "[1m]" marker in their CLI id (e.g. "opus[1m]",
        // "claude-opus-4-8[1m]"); everything else defaults to 200k. This is only a cold-start
        // guess — the real value replaces it as soon as a turn reports it.
        private static int Heuristic(string modelId)
        {
            return modelId.IndexOf("[1m]", StringComparison.OrdinalIgnoreCase) >= 0
                ? OneMillionContextWindow
                : DefaultContextWindow;
        }

        private static void EnsureHydrated()
        {
            if (_observed != null)
                return;

            _observed = new Dictionary<string, int>(StringComparer.Ordinal);

            var json = EditorPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return;

            try
            {
                var stored = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                if (stored == null)
                    return;

                foreach (var kvp in stored)
                {
                    if (!string.IsNullOrEmpty(kvp.Key) && kvp.Value > 0)
                        _observed[kvp.Key] = kvp.Value;
                }
            }
            catch
            {
                // Corrupt prefs payload — start fresh rather than throwing on a UI path.
            }
        }

        private static void Persist()
        {
            try
            {
                EditorPrefs.SetString(PrefsKey, JsonConvert.SerializeObject(_observed));
            }
            catch
            {
                // Best-effort persistence; a serialization/prefs failure must not break a turn.
            }
        }
    }
}
