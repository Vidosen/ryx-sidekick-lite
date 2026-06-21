// SPDX-License-Identifier: GPL-3.0-only
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class PlanToolRenderer : IToolElementRenderer
    {
        private readonly IMarkdownContentRenderer _markdown;

        internal PlanToolRenderer(IMarkdownContentRenderer markdown)
        {
            _markdown = markdown;
        }

        public bool CanRender(ToolUse toolUse)
        {
            if (toolUse == null) return false;
            var kind = ToolPresentationCatalog.GetEffectiveKind(toolUse);
            return kind == ToolKind.ImplementPlan || kind == ToolKind.ExitPlanMode;
        }

        public VisualElement BuildHeaderContent(ToolUse toolUse) => null;

        public VisualElement BuildBodyContent(ToolUse toolUse)
        {
            if (toolUse == null) return null;
            return CreateExitPlanModeContent(toolUse);
        }

        private VisualElement CreateExitPlanModeContent(ToolUse toolUse)
        {
            var container = new VisualElement();
            container.AddToClassList("sk-plan-content");

            // Extract plan from input
            var planText = "";
            if (toolUse.Input != null)
            {
                if (toolUse.Input.Type == JTokenType.Object && toolUse.Input["plan"] != null)
                {
                    planText = toolUse.Input["plan"].ToString();
                }
                else if (toolUse.Input.Type == JTokenType.String)
                {
                    planText = toolUse.Input.ToString();
                }
            }

            if (string.IsNullOrEmpty(planText))
            {
                return null; // Fall back to standard display
            }

            // Header: "Plan"
            var header = new Label("Plan");
            header.AddToClassList("sk-plan-header");
            container.Add(header);

            // Render plan content as markdown directly (no nested ScrollView - chat already scrolls)
            var ctx = new MarkdownRenderContext
            {
                UseRichTextForInlines = true,
                MaxNestingDepth = 6
            };
            var planContent = _markdown?.Render(planText, ctx);
            container.Add(planContent ?? new VisualElement());

            return container;
        }
    }
}
