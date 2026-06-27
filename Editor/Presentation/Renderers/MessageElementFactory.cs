// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Questions;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class MessageElementFactory : IMessageElementFactory
    {
        private readonly IMarkdownContentRenderer _markdownRenderer;
        private readonly MarkdownRenderContext _markdownContext;
        private readonly AttachmentController _attachmentController;
        private readonly Func<string, bool> _isToolAutoAccepted;
        private readonly IToolRendererRegistry _toolRendererRegistry;

        internal MessageElementFactory(
            IMarkdownContentRenderer markdownRenderer,
            MarkdownRenderContext markdownContext = null,
            AttachmentController attachmentController = null,
            Func<string, bool> isToolAutoAccepted = null,
            IToolRendererRegistry toolRendererRegistry = null) // TODO(T07-03 step 10): make required, remove default null
        {
            _markdownRenderer = markdownRenderer;
            _markdownContext = markdownContext;
            _attachmentController = attachmentController;
            _isToolAutoAccepted = isToolAutoAccepted ?? (_ => false);
            _toolRendererRegistry = toolRendererRegistry;
        }

        public VisualElement CreateMessageElement(Message message)
        {
            // System banner detection (domain reload, etc.)
            // Matches both the display tag and the CLI-stored prompt text
            if (message.Role == MessageRole.User && IsDomainReloadMessage(message.Content))
            {
                var banner = new SystemBannerElement(
                    SystemBannerElement.BannerType.DomainReload,
                    "Domain reload completed");
                banner.name = $"banner-{message.Id}";
                return banner;
            }

            // Tool messages are rendered inline as tool blocks
            if (message.Role == MessageRole.Tool)
            {
                if (message.ToolUses.Count > 0)
                {
                    var tool = message.ToolUses[0];
                    var element = CreateInlineToolElement(tool);
                    element.name = $"tool-{tool.Id}";
                    if (_isToolAutoAccepted(tool.Id))
                        (element as ToolCallElement)?.SetAutoAccepted(true);
                    return element;
                }
                return new VisualElement
                {
                    name = $"message-{message.Id}"
                };
            }

            // Thinking blocks rendered as ThinkingElement (before regular Assistant check)
            if (message.Role == MessageRole.Assistant && message.IsThinkingBlock)
            {
                var thinkingElement = new ThinkingElement();
                thinkingElement.name = $"thinking-{message.Id}";
                thinkingElement.SetThinking(message);
                return thinkingElement;
            }

            var bubble = new MessageBubbleElement
            {
                name = $"message-{message.Id}"
            };
            bubble.SetMessage(message, RenderMarkdownContent, _attachmentController);
            return bubble;
        }

        public VisualElement CreateMessageElement(Message message, bool showRoleHeader)
        {
            var element = CreateMessageElement(message);
            if (message != null
                && message.Role != MessageRole.Tool
                && !(message.Role == MessageRole.Assistant && message.IsThinkingBlock)
                && element is MessageBubbleElement bubble)
            {
                bubble.SetShowRoleHeader(showRoleHeader);
            }

            return element;
        }

        public void UpdateToolElement(ToolCallElement element, ToolUse toolUse) =>
            element.SetToolUse(
                toolUse,
                GetToolIcon,
                GetToolDisplayName,
                CreateToolSpecificContentInstance,
                CreateToolHeaderContentInstance,
                GetToolHeaderOptions);

        // internal static (тестируется CodexProviderTests)
        internal static string GetToolIcon(ToolKind kind)
        {
            return ToolPresentationCatalog.GetIconKey(kind);
        }

        /// <summary>
        /// Header presentation for the collapsed-foldable tool layout. Terminal/Bash and MCP tools
        /// opt in today; every other kind keeps the default (non-foldable) header.
        /// </summary>
        internal static ToolHeaderOptions GetToolHeaderOptions(ToolUse toolUse)
        {
            if (toolUse == null)
            {
                return default;
            }

            var kind = ToolPresentationCatalog.GetEffectiveKind(toolUse);

            if (kind == ToolKind.Bash)
            {
                var lines = BashCommandFormatter.CountLines(toolUse.Output);
                return new ToolHeaderOptions
                {
                    Foldable = true,
                    IconKey = ToolPresentationCatalog.GetIconKey(ToolKind.Bash),
                    MetaText = lines <= 0 ? null : (lines == 1 ? "1 line" : $"{lines} lines"),
                };
            }

            if (kind == ToolKind.Mcp)
            {
                // Monospace only when the header shows the technical server.tool address; a
                // human-readable title (when present) reads as prose.
                var hasTitle = !string.IsNullOrEmpty(ToolDisplayHelpers.ExtractMcpTitle(toolUse.Input));
                return new ToolHeaderOptions
                {
                    Foldable = true,
                    BadgeText = "MCP",
                    MonospaceName = !hasTitle,
                };
            }

            return default;
        }

        internal static string GetToolDisplayName(ToolUse toolUse)
        {
            if (toolUse == null)
            {
                return "tool";
            }

            var toolKind = ToolPresentationCatalog.GetEffectiveKind(toolUse);

            if (toolKind == ToolKind.Todo)
            {
                var todoCount = ParseTodoItems(toolUse.Input)?.Count ?? 0;
                return todoCount > 0 ? $"Tasks ({todoCount})" : "Tasks";
            }

            // Extract meaningful display name based on tool type
            // Note: Read, Edit, Write show file path in header content (AssetLinkElement/EditLinkElement)
            switch (toolKind)
            {
                case ToolKind.Read:
                    return "Read";
                case ToolKind.Edit:
                    return "Edit";
                case ToolKind.Write:
                    return "Write";
                case ToolKind.Move:
                    return "Move";
                case ToolKind.Bash:
                    // Description-led header (no "Bash:" prefix); fall back to a middle-ellipsized command.
                    var description = !string.IsNullOrWhiteSpace(toolUse.Description)
                        ? toolUse.Description
                        : ToolDisplayHelpers.ExtractDescription(toolUse.Input);
                    if (!string.IsNullOrEmpty(description))
                    {
                        return description;
                    }
                    var cmd = !string.IsNullOrWhiteSpace(toolUse.CommandLine)
                        ? toolUse.CommandLine
                        : ToolDisplayHelpers.ExtractCommand(toolUse.Input);
                    return !string.IsNullOrEmpty(cmd) ? BashCommandFormatter.MiddleEllipsis(cmd, 48) : "Bash";
                case ToolKind.Search:
                    var pattern = ExtractPattern(toolUse.Input);
                    return !string.IsNullOrEmpty(pattern) ? $"Search {pattern}" : "Search";
                case ToolKind.ListDirectory:
                    var targetPath = toolUse.FilePath;
                    if (string.IsNullOrEmpty(targetPath) && toolUse.Input?.Type == JTokenType.Object)
                    {
                        targetPath = toolUse.Input["path"]?.ToString();
                    }

                    return !string.IsNullOrEmpty(targetPath) ? $"LS {ToolDisplayHelpers.TrimPreview(targetPath, 30)}" : "LS";
                case ToolKind.AskUserQuestion:
                    var questionCount = AskUserQuestionTraceFormatter.GetQuestionCount(toolUse.Input);
                    if (questionCount <= 0)
                    {
                        return "Asked questions";
                    }

                    return questionCount == 1 ? "Asked 1 question" : $"Asked {questionCount} questions";
                case ToolKind.ImplementPlan:
                    return !string.IsNullOrWhiteSpace(toolUse.RawTitle) ? toolUse.RawTitle : "Implement plan";
                case ToolKind.ExitPlanMode:
                    return !string.IsNullOrWhiteSpace(toolUse.RawTitle) ? toolUse.RawTitle : "Plan";
                case ToolKind.WebFetch:
                    return "WebFetch";
                case ToolKind.WebSearch:
                    return "WebSearch";
                case ToolKind.Delete:
                    return "Delete";
                case ToolKind.Mcp:
                    // Lead with a human-readable title from the input when present (like Bash);
                    // otherwise fall back to the technical server.tool address.
                    var mcpTitle = ToolDisplayHelpers.ExtractMcpTitle(toolUse.Input);
                    return !string.IsNullOrEmpty(mcpTitle)
                        ? mcpTitle
                        : ToolPresentationCatalog.ResolveMcpDisplayName(toolUse);
                default:
                    return ToolPresentationCatalog.ResolveRawFallbackName(toolUse.RawTitle, toolUse.RawName, toolUse.Name);
            }
        }

        internal static bool HasTodoItems(JToken input)
        {
            return ParseTodoItems(input)?.Count > 0;
        }

        // private (instance)
        /// <summary>
        /// Creates an inline tool block element for VS Code-style rendering.
        /// </summary>
        private VisualElement CreateInlineToolElement(ToolUse toolUse)
        {
            var element = new ToolCallElement();
            element.AddToClassList("sk-inline-tool");
            element.SetToolUse(toolUse, GetToolIcon, GetToolDisplayName, CreateToolSpecificContentInstance, CreateToolHeaderContentInstance, GetToolHeaderOptions);
            return element;
        }

        private VisualElement CreateToolSpecificContentInstance(ToolUse toolUse)
        {
            if (toolUse == null) return null;
            var kind = ToolPresentationCatalog.GetEffectiveKind(toolUse);
            return _toolRendererRegistry?.Resolve(kind).BuildBodyContent(toolUse);
        }

        private VisualElement CreateToolHeaderContentInstance(ToolUse toolUse)
        {
            if (toolUse == null) return null;
            var kind = ToolPresentationCatalog.GetEffectiveKind(toolUse);
            return _toolRendererRegistry?.Resolve(kind).BuildHeaderContent(toolUse);
        }

        private VisualElement RenderMarkdownContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return new VisualElement();
            }

            var ctx = new MarkdownRenderContext
            {
                UseRichTextForInlines = _markdownContext?.UseRichTextForInlines ?? true,
                MaxNestingDepth = _markdownContext?.MaxNestingDepth ?? 6,
                OnLinkClicked = _markdownContext?.OnLinkClicked,
                OnCodeCopy = _markdownContext?.OnCodeCopy
            };

            return _markdownRenderer?.Render(content, ctx) ?? new VisualElement();
        }




        /// <summary>
        /// Detects domain reload messages by checking for display tag or CLI-stored prompt.
        /// </summary>
        internal static bool IsDomainReloadMessage(string content)
        {
            if (string.IsNullOrEmpty(content)) return false;

            // Match display tag
            if (content.Contains("<domain_reload/>")) return true;

            // Match CLI-stored prompt (for history loaded from JSONL)
            if (content.Contains("Unity recompilation and domain reload has completed")) return true;

            return false;
        }

        private static List<TodoItem> ParseTodoItems(JToken input)
        {
            if (input == null) return null;

            if (input.Type == JTokenType.Object && input["todos"] is JArray arr)
            {
                return arr
                    .OfType<JObject>()
                    .Select(t => new TodoItem
                    {
                        Content = t["content"]?.ToString(),
                        ActiveForm = t["activeForm"]?.ToString(),
                        Status = t["status"]?.ToString()
                    })
                    .Where(t => !string.IsNullOrEmpty(t.Content))
                    .ToList();
            }

            if (input.Type == JTokenType.String)
            {
                var parsed = JsonUtils.DeserializeToToken(input.ToString());
                if (parsed != null)
                {
                    return ParseTodoItems(parsed);
                }
            }

            return null;
        }

        private class TodoItem
        {
            public string Content;
            public string ActiveForm;
            public string Status; // pending, in_progress, completed, cancelled
        }

        private static string ExtractPattern(JToken input)
        {
            if (input == null) return "";
            if (input.Type == JTokenType.Object && input["pattern"] != null)
            {
                return input["pattern"]?.ToString();
            }

            if (input.Type == JTokenType.String)
            {
                var match = System.Text.RegularExpressions.Regex.Match(input.ToString(), @"pattern[""']?\s*[:""]?\s*[""']?([^""']+)[""']?");
                return match.Success ? match.Groups[1].Value : "";
            }

            return "";
        }


    }
}
