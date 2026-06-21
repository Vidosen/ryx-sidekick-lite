// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using Newtonsoft.Json.Linq;

namespace Ryx.Sidekick.Editor.Domain.Models
{
    /// <summary>
    /// Tracks a tool_use block being streamed, accumulating the JSON input.
    /// </summary>
    internal class PendingToolBlock
    {
        public string ToolUseId { get; set; }
        public string ToolName { get; set; }
        public int BlockIndex { get; set; }
        public StringBuilder InputJson { get; } = new();

        public JToken GetParsedInput()
        {
            var json = InputJson.ToString();
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JToken.Parse(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
