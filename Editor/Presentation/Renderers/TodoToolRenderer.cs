// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class TodoToolRenderer : IToolElementRenderer
    {
        public bool CanRender(ToolUse toolUse) =>
            toolUse != null && ToolPresentationCatalog.GetEffectiveKind(toolUse) == ToolKind.Todo;

        public VisualElement BuildHeaderContent(ToolUse toolUse) => null;

        public VisualElement BuildBodyContent(ToolUse toolUse)
        {
            if (toolUse == null) return null;
            return CreateTodoListContent(toolUse);
        }

        private static VisualElement CreateTodoListContent(ToolUse toolUse)
        {
            var todos = ParseTodoItems(toolUse.Input);
            if (todos == null || todos.Count == 0) return null;

            var container = new VisualElement();
            container.AddToClassList("sk-todo-list");

            // Progress summary header
            var completed = todos.Count(t => t.Status == "completed");
            var total = todos.Count;

            var header = new VisualElement();
            header.AddToClassList("sk-todo-header");

            // Progress bar
            var progressContainer = new VisualElement();
            progressContainer.AddToClassList("sk-todo-progress-container");

            var progressBar = new VisualElement();
            progressBar.AddToClassList("sk-todo-progress-bar");

            var progressFill = new VisualElement();
            progressFill.AddToClassList("sk-todo-progress-fill");
            var percentage = total > 0 ? (float)completed / total * 100f : 0f;
            progressFill.style.width = new StyleLength(new Length(percentage, LengthUnit.Percent));
            progressBar.Add(progressFill);

            progressContainer.Add(progressBar);

            var progressText = new Label($"{completed}/{total} completed");
            progressText.AddToClassList("sk-todo-progress-text");
            progressContainer.Add(progressText);

            header.Add(progressContainer);
            container.Add(header);

            // Todo items
            foreach (var todo in todos)
            {
                var todoItem = new VisualElement();
                todoItem.AddToClassList("sk-todo-item");
                todoItem.AddToClassList($"sk-todo-{todo.Status}");

                // Checkbox area
                var checkbox = new VisualElement();
                checkbox.AddToClassList("sk-todo-checkbox");

                var checkIcon = new Label(GetTodoCheckIcon(todo.Status));
                checkIcon.AddToClassList("sk-todo-check-icon");
                checkbox.Add(checkIcon);

                todoItem.Add(checkbox);

                // Content area
                var content = new VisualElement();
                content.AddToClassList("sk-todo-content");

                var text = new Label(todo.Content);
                text.AddToClassList("sk-todo-text");
                content.Add(text);

                // Show active form as subtitle when in progress
                if (todo.Status == "in_progress" && !string.IsNullOrEmpty(todo.ActiveForm))
                {
                    var activeLabel = new Label(todo.ActiveForm);
                    activeLabel.AddToClassList("sk-todo-active-form");
                    content.Add(activeLabel);
                }

                todoItem.Add(content);

                // Status badge
                var badge = new Label(GetTodoStatusLabel(todo.Status));
                badge.AddToClassList("sk-todo-badge");
                badge.AddToClassList($"sk-todo-badge-{todo.Status}");
                todoItem.Add(badge);

                container.Add(todoItem);
            }

            return container;
        }

        private class TodoItem
        {
            public string Content;
            public string ActiveForm;
            public string Status; // pending, in_progress, completed, cancelled
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

        private static string GetTodoCheckIcon(string status)
        {
            return status switch
            {
                "completed" => "✓",
                "in_progress" => "◐",
                "cancelled" => "✕",
                _ => "○" // pending
            };
        }

        private static string GetTodoStatusLabel(string status)
        {
            return status switch
            {
                "completed" => "DONE",
                "in_progress" => "IN PROGRESS",
                "cancelled" => "CANCELLED",
                _ => "PENDING"
            };
        }
    }
}
