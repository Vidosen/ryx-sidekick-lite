// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Questions;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class QuestionTraceRenderer : IToolElementRenderer
    {
        public bool CanRender(ToolUse toolUse) =>
            toolUse != null && ToolPresentationCatalog.GetEffectiveKind(toolUse) == ToolKind.AskUserQuestion;

        public VisualElement BuildHeaderContent(ToolUse toolUse) => null;

        public VisualElement BuildBodyContent(ToolUse toolUse)
        {
            if (toolUse == null) return null;
            return CreateAskUserQuestionContent(toolUse);
        }

        private static VisualElement CreateAskUserQuestionContent(ToolUse toolUse)
        {
            var prompt = AskUserQuestionTraceFormatter.GetQuestions(toolUse.Input);
            var questionCount = AskUserQuestionTraceFormatter.GetQuestionCount(toolUse.Input);
            if (prompt?.Questions == null || prompt.Questions.Count == 0 || questionCount == 0)
            {
                return null;
            }

            var answers = AskUserQuestionTraceFormatter.GetAnswers(toolUse.Input);
            var cancelled = AskUserQuestionTraceFormatter.IsCancelled(toolUse.Input);

            var container = new VisualElement();
            container.AddToClassList("sk-question-trace");

            var summaryRow = new VisualElement();
            summaryRow.AddToClassList("sk-question-trace__summary");

            var summaryLabel = new Label(BuildQuestionTraceSummary(prompt, answers, toolUse.Status, cancelled));
            summaryLabel.AddToClassList("sk-question-trace__summary-label");
            summaryRow.Add(summaryLabel);

            var toggleButton = new Button();
            toggleButton.text = toolUse.IsCollapsed ? "Show details" : "Hide details";
            toggleButton.AddToClassList("sk-question-trace__toggle");
            summaryRow.Add(toggleButton);

            container.Add(summaryRow);

            var details = new VisualElement();
            details.AddToClassList("sk-question-trace__details");
            details.style.display = toolUse.IsCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            container.Add(details);

            for (int i = 0; i < prompt.Questions.Count; i++)
            {
                var question = prompt.Questions[i];
                var item = new VisualElement();
                item.AddToClassList("sk-question-trace__item");

                var headerText = !string.IsNullOrWhiteSpace(question.Header)
                    ? question.Header
                    : $"Question {i + 1}";
                var questionHeader = new Label(headerText);
                questionHeader.AddToClassList("sk-question-trace__item-header");
                item.Add(questionHeader);

                var questionLabel = new Label(question.Question ?? string.Empty);
                questionLabel.AddToClassList("sk-question-trace__question");
                item.Add(questionLabel);

                var answerLabel = new Label(BuildQuestionAnswerText(question, answers, toolUse.Status, cancelled));
                answerLabel.AddToClassList("sk-question-trace__answer");
                item.Add(answerLabel);

                details.Add(item);
            }

            toggleButton.clicked += () =>
            {
                toolUse.IsCollapsed = !toolUse.IsCollapsed;
                toggleButton.text = toolUse.IsCollapsed ? "Show details" : "Hide details";
                details.style.display = toolUse.IsCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
            };

            return container;
        }

        private static string BuildQuestionTraceSummary(
            AskUserQuestionInput prompt,
            JObject answers,
            ToolStatus status,
            bool cancelled)
        {
            var questionCount = prompt?.Questions?.Count ?? 0;
            if (cancelled)
            {
                return "Question flow cancelled";
            }

            var answeredCount = CountAnsweredQuestions(answers);
            if (answeredCount > 0)
            {
                return answeredCount == questionCount
                    ? "Answers recorded"
                    : $"Answered {answeredCount} of {questionCount}";
            }

            return status == ToolStatus.Running
                ? "Waiting for response"
                : "No answer recorded";
        }

        private static string BuildQuestionAnswerText(
            AskUserQuestionItem question,
            JObject answers,
            ToolStatus status,
            bool cancelled)
        {
            if (cancelled)
            {
                return "Answer: Cancelled";
            }

            var selectedAnswers = ExtractQuestionAnswers(answers, AskUserQuestionTraceFormatter.GetTraceAnswerKey(question));
            if (selectedAnswers.Count > 0)
            {
                if (question?.IsSecret == true)
                {
                    return "Answer: Hidden";
                }

                return $"Answer: {string.Join(", ", selectedAnswers)}";
            }

            return status == ToolStatus.Running
                ? "Answer: Pending"
                : "Answer: No answer recorded";
        }

        private static int CountAnsweredQuestions(JObject answers)
        {
            if (answers == null)
            {
                return 0;
            }

            return answers.Properties()
                .Count(property => ExtractQuestionAnswers(answers, property.Name).Count > 0);
        }

        private static List<string> ExtractQuestionAnswers(JObject answers, string questionId)
        {
            if (answers == null || string.IsNullOrWhiteSpace(questionId))
            {
                return new List<string>();
            }

            return answers[questionId]?["answers"] is JArray answerArray
                ? answerArray.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).ToList()
                : new List<string>();
        }
    }
}
