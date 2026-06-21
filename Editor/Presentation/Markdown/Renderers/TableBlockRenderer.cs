// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class TableBlockRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 100;

        public bool CanRender(Block block) => block is Table;

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var table = (Table)block;
            var columnCount = table.ColumnDefinitions.Count;
            var columnWidth = columnCount > 0 ? 100f / columnCount : 100f;
            var lastRowIndex = table.Count - 1;

            var container = new VisualElement();
            container.AddToClassList(context.Class("table"));

            var isHeader = true;
            var rowIndex = 0;
            foreach (var rowBlock in table)
            {
                if (rowBlock is TableRow row)
                {
                    var rowElement = new VisualElement();
                    rowElement.AddToClassList(context.Class("table-row"));
                    if (isHeader && row.IsHeader)
                    {
                        rowElement.AddToClassList(context.Class("table-header"));
                    }
                    if (rowIndex == lastRowIndex)
                    {
                        rowElement.AddToClassList(context.Class("table-row-last"));
                    }

                    var cellIndex = 0;
                    var lastCellIndex = row.Count - 1;
                    foreach (var cellBlock in row)
                    {
                        if (cellBlock is TableCell cell)
                        {
                            var cellElement = new VisualElement();
                            cellElement.AddToClassList(context.Class("table-cell"));
                            
                            // Set equal column width for vertical alignment
                            cellElement.style.width = new Length(columnWidth, LengthUnit.Percent);
                            if (cellIndex == lastCellIndex)
                            {
                                cellElement.AddToClassList(context.Class("table-cell-last"));
                            }

                            renderChildren(cell, cellElement, context);

                            if (cell.ColumnIndex >= 0 && cell.ColumnIndex < columnCount)
                            {
                                var alignment = table.ColumnDefinitions[cell.ColumnIndex].Alignment;
                                if (alignment.HasValue)
                                {
                                    cellElement.AddToClassList(context.Class($"align-{alignment.Value.ToString().ToLowerInvariant()}"));
                                }
                            }

                            rowElement.Add(cellElement);
                            cellIndex++;
                        }
                    }

                    container.Add(rowElement);
                    isHeader = false;
                    rowIndex++;
                }
            }

            parent.Add(container);
        }
    }
}


