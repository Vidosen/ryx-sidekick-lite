// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal static class ToolDisplayHelpers
    {
        // Human-readable header candidates for MCP tools, in priority order (case-insensitive).
        private static readonly string[] McpTitleKeys = { "title", "description", "summary", "name", "label" };

        /// <summary>
        /// Picks a short, single-line, human-readable string from an MCP tool's input to lead the
        /// header (in place of the technical <c>server.tool</c> name), mirroring how Bash leads with
        /// its description. Returns null when no suitable field exists (long/multiline values like
        /// a code blob are rejected, so the address stays in the header instead).
        /// </summary>
        internal static string ExtractMcpTitle(JToken input)
        {
            if (input is not JObject obj)
            {
                return null;
            }

            foreach (var key in McpTitleKeys)
            {
                var prop = obj.Properties()
                    .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
                if (prop?.Value?.Type != JTokenType.String)
                {
                    continue;
                }

                var value = prop.Value.ToString().Trim();
                if (!string.IsNullOrEmpty(value) && value.Length <= 80 && value.IndexOf('\n') < 0)
                {
                    return value;
                }
            }

            return null;
        }

        internal static string TrimPreview(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length > maxLength ? value[..maxLength] + "..." : value;
        }

        internal static string ExtractCommand(JToken input)
        {
            if (input == null) return "";
            if (input.Type == JTokenType.Object)
            {
                var cmd = input["command"]?.ToString() ?? input["cmd"]?.ToString();
                return TrimPreview(cmd, 40);
            }

            if (input.Type == JTokenType.String)
            {
                var match = System.Text.RegularExpressions.Regex.Match(input.ToString(), @"(?:command|cmd)[""']?\s*[:""]?\s*[""']?([^""']+)[""']?");
                if (match.Success)
                {
                    var cmd = match.Groups[1].Value;
                    return TrimPreview(cmd, 40);
                }
            }

            return "";
        }

        internal static string ExtractDescription(JToken input)
        {
            if (input == null) return "";
            if (input.Type == JTokenType.Object && input["description"] != null)
            {
                return input["description"]?.ToString();
            }

            if (input.Type == JTokenType.String)
            {
                var match = System.Text.RegularExpressions.Regex.Match(input.ToString(), @"description[""']?\s*[:""]?\s*[""']?([^""']+)[""']?");
                return match.Success ? match.Groups[1].Value : "";
            }

            return "";
        }
    }
}
