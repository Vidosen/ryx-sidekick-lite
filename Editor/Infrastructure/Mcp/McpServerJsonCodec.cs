// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ryx.Sidekick.Editor.Infrastructure.Mcp
{
    /// <summary>
    /// Pure codec: serializes/deserializes <see cref="SidekickSettings.McpServerEntry"/> lists
    /// to/from the <c>{ "mcpServers": { name: {...} } }</c> JSON shape used by MCP config files.
    /// No Unity I/O, no settings mutation — suitable for unit testing without a live Editor.
    /// </summary>
    internal static class McpServerJsonCodec
    {
        /// <summary>
        /// Serializes all named entries (including disabled ones) to a pretty-printed JSON string
        /// in the <c>{ "mcpServers": { ... } }</c> wrapper format.
        /// Blank-named entries are skipped. Duplicate names: last wins.
        /// </summary>
        internal static string ToJson(IEnumerable<SidekickSettings.McpServerEntry> entries)
        {
            var servers = new JObject();
            foreach (var entry in entries ?? Enumerable.Empty<SidekickSettings.McpServerEntry>())
            {
                if (entry == null) continue;
                var name = entry.name?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                servers[name] = McpConfigManager.SerializeServerEntry(entry); // last-wins on dup name
            }
            return new JObject { ["mcpServers"] = servers }.ToString(Formatting.Indented);
        }

        /// <summary>
        /// Lenient parse of a JSON string into a list of <see cref="SidekickSettings.McpServerEntry"/>.
        /// Returns <c>true</c> and a populated <paramref name="entries"/> list on success;
        /// <c>false</c> with a human-readable <paramref name="error"/> message on failure.
        /// Never throws.
        /// </summary>
        internal static bool TryParse(string json, out List<SidekickSettings.McpServerEntry> entries, out string error)
        {
            entries = new List<SidekickSettings.McpServerEntry>();
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON is empty.";
                return false;
            }

            JToken root;
            try
            {
                root = JToken.Parse(json);
            }
            catch (JsonReaderException ex)
            {
                error = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            // Resolve the server map.
            JObject serverMap;
            if (root is JObject obj)
            {
                if (obj["mcpServers"] is JObject m)
                {
                    serverMap = m;
                }
                else
                {
                    // Treat the object itself as a bare { name: {...} } map.
                    serverMap = obj;
                }
            }
            else
            {
                error = "Expected a JSON object with an \"mcpServers\" map.";
                return false;
            }

            foreach (var property in serverMap.Properties())
            {
                // Non-object values are skipped: a bare single-server object with no name wrapper
                // (e.g. {"url":"u"}) yields an empty list + true. That's safe because Apply=Replace
                // routes through a drop guard that prompts before removing currently-configured servers.
                if (property.Value is not JObject server) continue;

                var e = new SidekickSettings.McpServerEntry
                {
                    id = Guid.NewGuid().ToString(),
                    name = property.Name,
                    enabled = true,
                    headers = new List<SidekickSettings.McpKeyValueEntry>(),
                    args = new List<string>(),
                    env = new List<SidekickSettings.McpKeyValueEntry>(),
                };

                // enabled
                var enabledToken = server["enabled"];
                if (enabledToken != null && enabledToken.Type == JTokenType.Boolean)
                {
                    e.enabled = enabledToken.Value<bool>();
                }

                // transport
                var typeStr = server["type"]?.ToString();
                string transport;
                if (string.IsNullOrEmpty(typeStr))
                {
                    // Infer from keys present
                    transport = server["command"] != null ? "stdio"
                        : server["url"] != null ? "http"
                        : "http";
                }
                else if (string.Equals(typeStr, "stdio", StringComparison.OrdinalIgnoreCase))
                {
                    transport = "stdio";
                }
                else
                {
                    // http / streamable-http / streamableHttp / sse / anything else → "http"
                    transport = "http";
                }
                e.transport = transport;

                if (string.Equals(transport, "stdio", StringComparison.Ordinal))
                {
                    e.command = server["command"]?.ToString();
                    e.args = (server["args"] as JArray)
                        ?.Select(t => t?.ToString() ?? string.Empty)
                        .ToList()
                        ?? new List<string>();
                    e.env = ParseKeyValues(server["env"] as JObject);
                    e.headers = new List<SidekickSettings.McpKeyValueEntry>();
                }
                else
                {
                    e.url = server["url"]?.ToString();
                    e.headers = ParseKeyValues(server["headers"] as JObject);
                    e.args = new List<string>();
                    e.env = new List<SidekickSettings.McpKeyValueEntry>();
                }

                entries.Add(e);
            }

            return true;
        }

        private static List<SidekickSettings.McpKeyValueEntry> ParseKeyValues(JObject o)
        {
            var result = new List<SidekickSettings.McpKeyValueEntry>();
            if (o == null) return result;
            foreach (var p in o.Properties())
            {
                result.Add(new SidekickSettings.McpKeyValueEntry
                {
                    key = p.Name,
                    value = p.Value?.ToString() ?? string.Empty
                });
            }
            return result;
        }
    }
}
