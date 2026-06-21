// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class FileToolRenderer : IToolElementRenderer
    {
        public bool CanRender(ToolUse toolUse)
        {
            if (toolUse == null) return false;
            var kind = ToolPresentationCatalog.GetEffectiveKind(toolUse);
            return kind is ToolKind.Read or ToolKind.Write or ToolKind.Edit;
        }

        public VisualElement BuildHeaderContent(ToolUse toolUse)
        {
            if (toolUse == null) return null;
            var toolKind = ToolPresentationCatalog.GetEffectiveKind(toolUse);

            if (toolKind == ToolKind.Read)
            {
                var filePath = ExtractFilePath(toolUse.Input);
                if (!string.IsNullOrEmpty(filePath))
                {
                    return new AssetLinkElement(filePath);
                }
            }

            if (toolKind is ToolKind.Write or ToolKind.Edit)
            {
                var filePath = !string.IsNullOrEmpty(toolUse.FilePath)
                    ? toolUse.FilePath
                    : ExtractFilePath(toolUse.Input);

                if (!string.IsNullOrEmpty(filePath))
                {
                    var lineNumber = ExtractFirstChangedLine(toolUse.DiffContent);
                    // Try DiffContent first, fallback to computing from Input
                    var summary = CalculateDiffSummary(toolUse.DiffContent)
                                  ?? CalculateDiffSummaryFromInput(toolUse.Input);
                    return new EditLinkElement(filePath, lineNumber, summary);
                }
            }

            return null;
        }

        public VisualElement BuildBodyContent(ToolUse toolUse) => null;

        private static string ExtractFilePath(JToken input)
        {
            if (input == null) return "";
            if (input.Type == JTokenType.Object)
            {
                return input["file_path"]?.ToString() ??
                       input["filePath"]?.ToString() ??
                       input["path"]?.ToString() ??
                       "";
            }

            if (input.Type == JTokenType.String)
            {
                var match = System.Text.RegularExpressions.Regex.Match(input.ToString(), @"file_path[""']?\s*[:""]?\s*[""']?([^""',}\s]+)");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }

                match = System.Text.RegularExpressions.Regex.Match(input.ToString(), @"path[""']?\s*[:""]?\s*[""']?([^""',}\s]+)");
                return match.Success ? match.Groups[1].Value : "";
            }

            return "";
        }

        /// <summary>
        /// Extracts the line number of the first change from a unified diff.
        /// Parses hunk headers (@@ -X,Y +Z,W @@) to find the starting line in the new file.
        /// </summary>
        private static int ExtractFirstChangedLine(string diff)
        {
            if (string.IsNullOrEmpty(diff))
                return 1;

            // Parse standard unified diff hunk header: @@ -X,Y +Z,W @@
            var hunkMatch = System.Text.RegularExpressions.Regex.Match(
                diff,
                @"@@\s*-\d+(?:,\d+)?\s*\+(\d+)(?:,\d+)?\s*@@");

            if (hunkMatch.Success && int.TryParse(hunkMatch.Groups[1].Value, out int hunkLine))
                return hunkLine;

            // Fallback: count to first '+' line
            var lines = diff.Split(new[] { "\n" }, StringSplitOptions.None);
            int lineNumber = 1;

            foreach (var line in lines)
            {
                if (line.StartsWith("---") || line.StartsWith("+++") || line.StartsWith("@@"))
                    continue;

                if (line.StartsWith("+"))
                    return lineNumber;

                if (!line.StartsWith("-"))
                    lineNumber++;
            }

            return 1;
        }

        /// <summary>
        /// Calculates a human-readable summary of changes from a diff.
        /// Returns format like "Added 33 lines" or "Removed 5 lines".
        /// </summary>
        private static string CalculateDiffSummary(string diff)
        {
            if (string.IsNullOrEmpty(diff))
                return null;

            int added = 0;
            int removed = 0;

            var lines = diff.Split(new[] { "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (line.StartsWith("+") && !line.StartsWith("+++"))
                    added++;
                else if (line.StartsWith("-") && !line.StartsWith("---"))
                    removed++;
            }

            if (added == 0 && removed == 0)
                return null;

            var parts = new List<string>();
            if (added > 0)
                parts.Add($"Added {added} {(added == 1 ? "line" : "lines")}");
            if (removed > 0)
                parts.Add($"Removed {removed} {(removed == 1 ? "line" : "lines")}");

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Calculates diff summary from Edit tool input (old_string/new_string).
        /// Used as fallback when DiffContent is not available.
        /// </summary>
        private static string CalculateDiffSummaryFromInput(JToken input)
        {
            if (input == null) return null;

            var oldString = input["old_string"]?.ToString() ?? "";
            var newString = input["new_string"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(oldString) && string.IsNullOrEmpty(newString))
                return null;

            var oldLines = string.IsNullOrEmpty(oldString) ? 0 : oldString.Split('\n').Length;
            var newLines = string.IsNullOrEmpty(newString) ? 0 : newString.Split('\n').Length;

            var diff = newLines - oldLines;

            if (diff > 0)
                return $"Added {diff} {(diff == 1 ? "line" : "lines")}";
            if (diff < 0)
                return $"Removed {-diff} {(-diff == 1 ? "line" : "lines")}";

            // Same number of lines but content changed
            if (!string.IsNullOrEmpty(oldString) && oldString != newString)
                return $"Changed {newLines} {(newLines == 1 ? "line" : "lines")}";

            return null;
        }
    }
}
