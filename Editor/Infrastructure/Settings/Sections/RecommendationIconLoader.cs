// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// In-memory, per-session icon cache for MCP recommendation cards (B4).
    /// Loads textures via UnityWebRequestTexture; invokes the callback on the main thread
    /// via the async-operation completed callback (which Unity fires on the main thread).
    /// A malformed URL or network failure is silently swallowed — the fallback letter tile stays visible.
    /// </summary>
    internal sealed class RecommendationIconLoader
    {
        // Shared across all section instances (per session); keyed by URL.
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public void Load(string url, Action<Texture2D> onLoaded)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return;

                if (Cache.TryGetValue(url, out var cached) && cached != null)
                {
                    onLoaded?.Invoke(cached);
                    return;
                }

                var req = UnityWebRequestTexture.GetTexture(url);
                req.timeout = 8;
                var op = req.SendWebRequest();
                op.completed += _ =>
                {
                    try
                    {
                        if (req.result == UnityWebRequest.Result.Success)
                        {
                            var tex = DownloadHandlerTexture.GetContent(req);
                            Cache[url] = tex;
                            onLoaded?.Invoke(tex);
                        }
                    }
                    catch { /* swallow — fallback letter stays */ }
                    finally { req.Dispose(); }
                };
            }
            catch { /* swallow malformed URL or init errors */ }
        }
    }
}
