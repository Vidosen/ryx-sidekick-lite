// SPDX-License-Identifier: GPL-3.0-only
using Newtonsoft.Json.Linq;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal static class ToolDisplayHelpers
    {
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
