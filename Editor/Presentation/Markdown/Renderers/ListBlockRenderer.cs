// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class ListBlockRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 100;

        public bool CanRender(Block block) => block is ListBlock;

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var list = (ListBlock)block;

            if (context.MaxNestingDepth > 0 && context.CurrentDepth >= context.MaxNestingDepth)
            {
                var truncated = new Label("[List truncated - max depth reached]");
                truncated.AddToClassList(context.Class("truncated"));
                parent.Add(truncated);
                return;
            }

            var container = new VisualElement();
            container.AddToClassList(context.Class("list"));
            container.AddToClassList(list.IsOrdered ? context.Class("ol") : context.Class("ul"));

            int index = 0;
            if (list.IsOrdered)
            {
                index = 1;
                if (!string.IsNullOrEmpty(list.OrderedStart) && int.TryParse(list.OrderedStart, out var parsedStart))
                {
                    index = parsedStart > 0 ? parsedStart : 1;
                }
            }

            foreach (var item in list)
            {
                if (item is ListItemBlock listItem)
                {
                    var itemElement = CreateListItem(listItem, list.IsOrdered, index, context, renderChildren);
                    container.Add(itemElement);
                    index++;
                }
            }

            parent.Add(container);
        }

        private VisualElement CreateListItem(ListItemBlock item, bool isOrdered, int index,
            MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var row = new VisualElement();
            row.AddToClassList(context.Class("list-item"));

            var marker = new Label(isOrdered ? $"{index}." : "•");
            marker.AddToClassList(context.Class("list-marker"));
            row.Add(marker);

            var content = new VisualElement();
            content.AddToClassList(context.Class("list-content"));

            context.CurrentDepth++;
            renderChildren(item, content, context);
            context.CurrentDepth--;

            row.Add(content);

            if (item.Count > 0 && item[0] is ParagraphBlock { Inline: { FirstChild: Markdig.Extensions.TaskLists.TaskList task } })
            {
                row.AddToClassList(context.Class("task-item"));
                row.AddToClassList(task.Checked ? context.Class("task-checked") : context.Class("task-unchecked"));
                marker.text = task.Checked ? "☑" : "☐";
            }

            return row;
        }
    }
}


