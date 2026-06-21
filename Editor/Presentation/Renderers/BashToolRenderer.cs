// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class BashToolRenderer : IToolElementRenderer
    {
        public bool CanRender(ToolUse toolUse) =>
            toolUse != null && ToolPresentationCatalog.GetEffectiveKind(toolUse) == ToolKind.Bash;

        public VisualElement BuildHeaderContent(ToolUse toolUse) => null;

        public VisualElement BuildBodyContent(ToolUse toolUse)
        {
            if (toolUse == null) return null;

            var container = new VisualElement();
            container.AddToClassList("sk-bash-content");

            var command = !string.IsNullOrWhiteSpace(toolUse.CommandLine)
                ? ToolDisplayHelpers.TrimPreview(toolUse.CommandLine, 40)
                : ToolDisplayHelpers.ExtractCommand(toolUse.Input);
            var description = ToolDisplayHelpers.ExtractDescription(toolUse.Input);

            // Description subtitle (if available)
            if (!string.IsNullOrEmpty(description))
            {
                var descLabel = new Label(description);
                descLabel.AddToClassList("sk-bash-description");
                container.Add(descLabel);
            }

            // IN row: command
            if (!string.IsNullOrEmpty(command))
            {
                var cmdRow = new VisualElement();
                cmdRow.AddToClassList("sk-bash-cmd-row");

                var inLabel = new Label("IN");
                inLabel.AddToClassList("sk-tool-io-label");
                cmdRow.Add(inLabel);

                var cmdLabel = new Label(command);
                cmdLabel.AddToClassList("sk-bash-cmd");
                cmdRow.Add(cmdLabel);

                container.Add(cmdRow);
            }

            if (toolUse.TerminalInputEvents != null)
            {
                foreach (var terminalInputEvent in toolUse.TerminalInputEvents)
                {
                    if (terminalInputEvent == null || string.IsNullOrWhiteSpace(terminalInputEvent.DisplayText))
                    {
                        continue;
                    }

                    var inputRow = new VisualElement();
                    inputRow.AddToClassList("sk-bash-cmd-row");

                    var inputLabel = new Label("IN");
                    inputLabel.AddToClassList("sk-tool-io-label");
                    inputRow.Add(inputLabel);

                    var inputValue = new Label(terminalInputEvent.DisplayText);
                    inputValue.AddToClassList("sk-bash-cmd");
                    inputRow.Add(inputValue);

                    container.Add(inputRow);
                }
            }

            // OUT row: output
            var output = toolUse.Output ?? "";
            if (!string.IsNullOrEmpty(output))
            {
                var outRow = new VisualElement();
                outRow.AddToClassList("sk-bash-out-row");

                var outLabel = new Label("OUT");
                outLabel.AddToClassList("sk-tool-io-label");
                outRow.Add(outLabel);

                var lines = output.Split('\n');
                var isMultiline = lines.Length > 1;

                if (isMultiline)
                {
                    // Preview (first line or truncated)
                    var previewText = lines[0].Length > 60 ? lines[0].Substring(0, 60) + "..." : lines[0];
                    if (lines.Length > 1) previewText += $" (+{lines.Length - 1} lines)";

                    var previewLabel = new Label(previewText);
                    previewLabel.AddToClassList("sk-bash-out-preview");
                    outRow.Add(previewLabel);

                    // Expand button
                    var expandBtn = new Button();
                    expandBtn.text = toolUse.IsOutputExpanded ? "▼" : "▶";
                    expandBtn.AddToClassList("sk-bash-expand-btn");
                    outRow.Add(expandBtn);

                    container.Add(outRow);

                    // Expandable output panel
                    var outputPanel = new VisualElement();
                    outputPanel.AddToClassList("sk-bash-output-panel");
                    outputPanel.style.display = toolUse.IsOutputExpanded ? DisplayStyle.Flex : DisplayStyle.None;

                    var scroll = new ScrollView(ScrollViewMode.Vertical);
                    scroll.AddToClassList("sk-bash-output-scroll");

                    var outputText = new Label(output);
                    outputText.AddToClassList("sk-bash-output-text");
                    outputText.selection.isSelectable = true;
                    scroll.Add(outputText);

                    // Copy button
                    var copyBtn = new Button(() => UnityEngine.GUIUtility.systemCopyBuffer = output);
                    copyBtn.text = "Copy";
                    copyBtn.AddToClassList("sk-bash-copy-btn");

                    var toolbar = new VisualElement();
                    toolbar.AddToClassList("sk-bash-output-toolbar");
                    toolbar.Add(copyBtn);

                    outputPanel.Add(scroll);
                    outputPanel.Add(toolbar);

                    container.Add(outputPanel);

                    // Toggle handler
                    expandBtn.clicked += () =>
                    {
                        toolUse.IsOutputExpanded = !toolUse.IsOutputExpanded;
                        expandBtn.text = toolUse.IsOutputExpanded ? "▼" : "▶";
                        outputPanel.style.display = toolUse.IsOutputExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                    };
                }
                else
                {
                    // Single line output - show inline
                    var outputLabel = new Label(output);
                    outputLabel.AddToClassList("sk-bash-out-inline");
                    outRow.Add(outputLabel);

                    container.Add(outRow);
                }
            }
            else if (toolUse.Status == ToolStatus.Running)
            {
                // Show running indicator if no output yet
                var outRow = new VisualElement();
                outRow.AddToClassList("sk-bash-out-row");

                var outLabel = new Label("OUT");
                outLabel.AddToClassList("sk-tool-io-label");
                outRow.Add(outLabel);

                var runningLabel = new Label("...");
                runningLabel.AddToClassList("sk-bash-out-running");
                outRow.Add(runningLabel);

                container.Add(outRow);
            }

            return container;
        }
    }
}
