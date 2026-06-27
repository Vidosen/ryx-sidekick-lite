// SPDX-License-Identifier: GPL-3.0-only
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using UnityEngine;
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

            // The full command (CommandLine, or the raw "command"/"cmd" from Input) — never truncated here.
            var command = ResolveFullCommand(toolUse);
            if (!string.IsNullOrEmpty(command))
            {
                container.Add(BuildCommandBlock(command));
            }

            // Interactive terminal input (stdin sent into a running session) rendered as $-prompt rows.
            if (toolUse.TerminalInputEvents != null)
            {
                foreach (var terminalInputEvent in toolUse.TerminalInputEvents)
                {
                    if (terminalInputEvent == null || string.IsNullOrWhiteSpace(terminalInputEvent.DisplayText))
                    {
                        continue;
                    }

                    container.Add(BuildCommandBlock(terminalInputEvent.DisplayText));
                }
            }

            var output = toolUse.Output ?? "";
            if (!string.IsNullOrEmpty(output))
            {
                var panel = new VisualElement();
                panel.AddToClassList("sk-bash-output-panel");

                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.AddToClassList("sk-bash-output-scroll");

                var outputText = new Label(output);
                outputText.AddToClassList("sk-bash-output-text");
                if (toolUse.Status == ToolStatus.Error)
                {
                    outputText.AddToClassList("sk-bash-output-text--error");
                }
                outputText.selection.isSelectable = true;
                scroll.Add(outputText);
                panel.Add(scroll);

                container.Add(panel);
            }
            else if (toolUse.Status == ToolStatus.Running)
            {
                // No output yet — show a muted running indicator.
                var runningLabel = new Label("Running…");
                runningLabel.AddToClassList("sk-bash-out-running");
                container.Add(runningLabel);
            }

            // Footer: copy actions for whatever is available.
            var footer = BuildFooter(command, output);
            if (footer != null)
            {
                container.Add(footer);
            }

            return container;
        }

        private static VisualElement BuildCommandBlock(string command)
        {
            var block = new VisualElement();
            block.AddToClassList("sk-bash-cmd-block");

            var prompt = new Label("$");
            prompt.AddToClassList("sk-bash-cmd-prompt");
            block.Add(prompt);

            var cmdWrap = new VisualElement();
            cmdWrap.AddToClassList("sk-bash-cmd-text");

            foreach (var segment in BashCommandFormatter.Segment(command))
            {
                var segLabel = new Label(segment.Text);
                segLabel.AddToClassList("sk-bash-cmd-seg");
                segLabel.AddToClassList(segment.Role == BashCommandFormatter.CommandRole.Prefix
                    ? "sk-bash-cmd-seg--dim"
                    : "sk-bash-cmd-seg--action");
                segLabel.selection.isSelectable = true;
                cmdWrap.Add(segLabel);
            }

            block.Add(cmdWrap);
            return block;
        }

        private static VisualElement BuildFooter(string command, string output)
        {
            var hasCommand = !string.IsNullOrEmpty(command);
            var hasOutput = !string.IsNullOrEmpty(output);
            if (!hasCommand && !hasOutput)
            {
                return null;
            }

            var footer = new VisualElement();
            footer.AddToClassList("sk-bash-footer");

            if (hasCommand)
            {
                var copyCmd = new Button(() => GUIUtility.systemCopyBuffer = command) { text = "Copy command" };
                copyCmd.AddToClassList("sk-bash-footer-btn");
                footer.Add(copyCmd);
            }

            if (hasOutput)
            {
                var copyOut = new Button(() => GUIUtility.systemCopyBuffer = output) { text = "Copy output" };
                copyOut.AddToClassList("sk-bash-footer-btn");
                footer.Add(copyOut);
            }

            return footer;
        }

        private static string ResolveFullCommand(ToolUse toolUse)
        {
            if (!string.IsNullOrWhiteSpace(toolUse.CommandLine))
            {
                return toolUse.CommandLine;
            }

            if (toolUse.Input?.Type == JTokenType.Object)
            {
                return toolUse.Input["command"]?.ToString()
                    ?? toolUse.Input["cmd"]?.ToString()
                    ?? "";
            }

            return "";
        }
    }
}
