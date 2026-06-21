// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Ryx.Sidekick.Editor;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Contracts;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class DefaultToolElementRendererFactory : IToolElementRendererFactory
    {
        private readonly IMarkdownContentRenderer _markdown;

        public DefaultToolElementRendererFactory(IMarkdownContentRenderer markdown)
        {
            _markdown = markdown;
        }

        public IReadOnlyDictionary<ToolKind, IToolElementRenderer> CreateRendererMap()
        {
            var bash = new BashToolRenderer();
            var todo = new TodoToolRenderer();
            var file = new FileToolRenderer();
            var question = new QuestionTraceRenderer();
            var plan = new PlanToolRenderer(_markdown);

            return new Dictionary<ToolKind, IToolElementRenderer>
            {
                { ToolKind.Bash, bash },
                { ToolKind.Todo, todo },
                { ToolKind.Read, file },
                { ToolKind.Write, file },
                { ToolKind.Edit, file },
                { ToolKind.AskUserQuestion, question },
                { ToolKind.ImplementPlan, plan },
                { ToolKind.ExitPlanMode, plan },
            };
        }

        public IToolElementRenderer CreateFallbackRenderer() => new GenericToolRenderer();
    }
}
