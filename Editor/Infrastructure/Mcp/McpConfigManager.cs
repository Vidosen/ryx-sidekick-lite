// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Infrastructure;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure.Mcp
{
    /// <summary>
    /// Generates and manages MCP config files for Claude CLI.
    /// </summary>
    internal sealed class McpConfigManager : IDisposable
    {
        private string _mcpConfigPath;
        private bool _ownsMcpConfig;

        /// <summary>
        /// Prepare MCP config and return additional CLI arguments.
        /// </summary>
        public bool Prepare(out string additionalArgs)
        {
            additionalArgs = string.Empty;
            Cleanup();

            var settings = SidekickSettings.instance;
            if (!settings.EnableMcpConfig)
            {
                return true;
            }

            string resolvedCustomConfig = null;
            if (!string.IsNullOrEmpty(settings.McpConfigPath))
            {
                resolvedCustomConfig = Path.IsPathRooted(settings.McpConfigPath)
                    ? settings.McpConfigPath
                    : Path.GetFullPath(Path.Combine(Application.dataPath, "..", settings.McpConfigPath));
            }

            var useCustomConfig = settings.UseCustomMcpConfig &&
                                  !string.IsNullOrEmpty(resolvedCustomConfig) &&
                                  File.Exists(resolvedCustomConfig);

            if (useCustomConfig)
            {
                _mcpConfigPath = resolvedCustomConfig;
            }
            else
            {
                _mcpConfigPath = ResolveConfigPath(settings.GeneratedMcpConfigPath);
                EnsureConfigDirectory();
                WriteGeneratedConfig();
            }

            additionalArgs = $"--mcp-config \"{_mcpConfigPath}\"";

            // NOTE: --permission-prompt-tool stdio is now always added by SidekickSettings.BuildArguments
            // when using bidirectional stream-json (streamingInput=true). We only add a custom tool here
            // if explicitly configured.
            if (!string.IsNullOrEmpty(settings.McpPermissionPromptTool))
            {
                additionalArgs = $"{additionalArgs} --permission-prompt-tool {settings.McpPermissionPromptTool}";
            }

            return true;
        }

        public IReadOnlyDictionary<string, JObject> LoadMcpServers()
        {
            Cleanup();

            var settings = SidekickSettings.instance;
            if (!settings.EnableMcpConfig)
            {
                return new Dictionary<string, JObject>();
            }

            string resolvedCustomConfig = null;
            if (!string.IsNullOrEmpty(settings.McpConfigPath))
            {
                resolvedCustomConfig = Path.IsPathRooted(settings.McpConfigPath)
                    ? settings.McpConfigPath
                    : Path.GetFullPath(Path.Combine(Application.dataPath, "..", settings.McpConfigPath));
            }

            var useCustomConfig = settings.UseCustomMcpConfig &&
                                  !string.IsNullOrEmpty(resolvedCustomConfig) &&
                                  File.Exists(resolvedCustomConfig);

            if (useCustomConfig)
            {
                _mcpConfigPath = resolvedCustomConfig;
            }
            else
            {
                _mcpConfigPath = ResolveConfigPath(settings.GeneratedMcpConfigPath);
                EnsureConfigDirectory();
                WriteGeneratedConfig();
            }

            if (string.IsNullOrEmpty(_mcpConfigPath) || !File.Exists(_mcpConfigPath))
            {
                return new Dictionary<string, JObject>();
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(_mcpConfigPath));
                if (root["mcpServers"] is not JObject servers)
                {
                    return new Dictionary<string, JObject>();
                }

                var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
                foreach (var property in servers.Properties())
                {
                    if (property.Value is JObject server)
                    {
                        result[property.Name] = (JObject)server.DeepClone();
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[McpConfigManager] Failed to load MCP servers: {ex.Message}");
                return new Dictionary<string, JObject>();
            }
        }

        public void Cleanup()
        {
            if (_ownsMcpConfig && !string.IsNullOrEmpty(_mcpConfigPath) && File.Exists(_mcpConfigPath))
            {
                try
                {
                    File.Delete(_mcpConfigPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[McpConfigManager] Failed to delete MCP config: {ex.Message}");
                }
            }

            _mcpConfigPath = null;
            _ownsMcpConfig = false;
        }

        public void Dispose()
        {
            Cleanup();
        }

        private static string ResolveConfigPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return Path.Combine(Application.dataPath, "..", "Sidekick", "mcp-config.generated.json");
            }

            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
        }

        private void EnsureConfigDirectory()
        {
            var dir = Path.GetDirectoryName(_mcpConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private void WriteGeneratedConfig()
        {
            _ownsMcpConfig = false; // keep on disk by default

            var servers = new JObject();
            foreach (var pair in BuildMcpServerDictionary())
            {
                servers[pair.Key] = pair.Value;
            }

            var config = new JObject
            {
                ["mcpServers"] = servers
            };

            var configJson = config.ToString(Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(_mcpConfigPath, configJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (SidekickSettings.instance.VerboseLogging)
            {
                Debug.Log($"[McpConfigManager] MCP config written to {_mcpConfigPath}");
            }
        }

        /// <summary>
        /// Builds the <c>mcpServers</c> map (server name -> JObject) from the configured server list.
        /// Disabled entries and entries with a blank name are skipped. Consumed by both the Claude
        /// generated config and Codex config overrides.
        /// </summary>
        internal IReadOnlyDictionary<string, JObject> BuildMcpServerDictionary()
        {
            var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var entry in SidekickSettings.instance.GetMcpServers())
            {
                if (entry == null || !entry.enabled)
                {
                    continue;
                }

                var name = entry.name?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                result[name] = SerializeServerEntry(entry);
            }

            return result;
        }

        /// <summary>
        /// Serializes a single <see cref="SidekickSettings.McpServerEntry"/> into the JObject shape used
        /// by the MCP config map. Emits an explicit <c>type</c> for transport disambiguation; entry fields
        /// are used verbatim (no gateway URL resolution since B1).
        /// </summary>
        internal static JObject SerializeServerEntry(SidekickSettings.McpServerEntry entry)
        {
            var obj = new JObject();
            var transport = string.IsNullOrWhiteSpace(entry.transport)
                ? "http"
                : entry.transport.Trim().ToLowerInvariant();

            if (transport == "stdio")
            {
                obj["type"] = "stdio";
                obj["command"] = entry.command ?? string.Empty;

                if (entry.args != null && entry.args.Count > 0)
                {
                    obj["args"] = new JArray(entry.args.Where(arg => arg != null).Cast<object>().ToArray());
                }

                var env = BuildKeyValueObject(entry.env);
                if (env != null)
                {
                    obj["env"] = env;
                }
            }
            else
            {
                obj["type"] = "http";

                var url = entry.url;
                if (string.IsNullOrEmpty(url))
                {
                    url = "http://localhost:8080/mcp";
                }

                obj["url"] = url;

                var headers = BuildKeyValueObject(entry.headers);
                if (headers != null)
                {
                    obj["headers"] = headers;
                }
            }

            obj["enabled"] = entry.enabled;
            return obj;
        }

        private static JObject BuildKeyValueObject(List<SidekickSettings.McpKeyValueEntry> entries)
        {
            if (entries == null)
            {
                return null;
            }

            JObject result = null;
            foreach (var entry in entries)
            {
                var key = entry?.key?.Trim();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                result ??= new JObject();
                result[key] = entry.value ?? string.Empty;
            }

            return result;
        }

    }
}
