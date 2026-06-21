// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal sealed class AskUserQuestionSessionState
    {
        private readonly Dictionary<int, HashSet<int>> _selections = new();
        private readonly Dictionary<int, string> _otherTexts = new();

        public AskUserQuestionSessionState(AskUserQuestionInput input)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));

            for (int i = 0; i < Input.Questions.Count; i++)
            {
                _selections[i] = new HashSet<int>();
            }
        }

        public AskUserQuestionInput Input { get; }

        public int QuestionCount => Input.Questions.Count;

        public IReadOnlyCollection<int> GetSelectedIndices(int questionIndex)
        {
            return _selections.TryGetValue(questionIndex, out var selected)
                ? selected
                : Array.Empty<int>();
        }

        public string GetOtherText(int questionIndex)
        {
            return _otherTexts.TryGetValue(questionIndex, out var value) ? value : string.Empty;
        }

        public void SetOtherText(int questionIndex, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                _otherTexts.Remove(questionIndex);
                return;
            }

            _otherTexts[questionIndex] = value;
        }

        public void SelectOption(int questionIndex, int optionIndex, bool isMultiSelect)
        {
            if (!_selections.TryGetValue(questionIndex, out var selected))
            {
                selected = new HashSet<int>();
                _selections[questionIndex] = selected;
            }

            if (isMultiSelect)
            {
                if (selected.Contains(optionIndex))
                {
                    selected.Remove(optionIndex);
                }
                else
                {
                    selected.Add(optionIndex);
                }

                return;
            }

            selected.Clear();
            selected.Add(optionIndex);
        }

        public int CountAnsweredQuestions()
        {
            int count = 0;

            for (int i = 0; i < QuestionCount; i++)
            {
                if (IsQuestionAnswered(i))
                {
                    count++;
                }
            }

            return count;
        }

        public bool AreAllQuestionsAnswered()
        {
            for (int i = 0; i < QuestionCount; i++)
            {
                if (!IsQuestionAnswered(i))
                {
                    return false;
                }
            }

            return QuestionCount > 0;
        }

        public bool IsQuestionAnswered(int questionIndex)
        {
            var question = GetQuestion(questionIndex);
            if (question == null)
            {
                return false;
            }

            var otherText = GetOtherText(questionIndex);
            var hasQuestionLevelOtherAnswer = question.IsOther && !string.IsNullOrWhiteSpace(otherText);

            if (!_selections.TryGetValue(questionIndex, out var selected) || selected.Count == 0)
            {
                return hasQuestionLevelOtherAnswer;
            }

            foreach (var optionIndex in selected)
            {
                if (optionIndex < 0 || optionIndex >= question.Options.Count)
                {
                    continue;
                }

                if (IsOtherOption(question.Options[optionIndex]) && string.IsNullOrWhiteSpace(otherText))
                {
                    return false;
                }
            }

            return true;
        }

        public int FindNextUnansweredQuestion(int currentIndex)
        {
            if (QuestionCount == 0)
            {
                return -1;
            }

            for (int i = 1; i < QuestionCount; i++)
            {
                var candidateIndex = (currentIndex + i) % QuestionCount;
                if (!IsQuestionAnswered(candidateIndex))
                {
                    return candidateIndex;
                }
            }

            return -1;
        }

        public IReadOnlyList<string> GetResolvedLabels(int questionIndex)
        {
            var question = GetQuestion(questionIndex);
            if (question == null)
            {
                return Array.Empty<string>();
            }

            var otherText = GetOtherText(questionIndex);

            if (!_selections.TryGetValue(questionIndex, out var selected) || selected.Count == 0)
            {
                return question.IsOther && !string.IsNullOrWhiteSpace(otherText)
                    ? new[] { otherText.Trim() }
                    : Array.Empty<string>();
            }

            var resolved = new List<string>();

            foreach (var optionIndex in selected.OrderBy(index => index))
            {
                if (optionIndex < 0 || optionIndex >= question.Options.Count)
                {
                    continue;
                }

                var option = question.Options[optionIndex];
                var label = option.Label ?? string.Empty;

                if (IsOtherOption(option) && !string.IsNullOrWhiteSpace(otherText))
                {
                    resolved.Add(otherText.Trim());
                }
                else
                {
                    resolved.Add(label);
                }
            }

            return resolved;
        }

        public string BuildSummary()
        {
            var parts = new List<string>();

            for (int i = 0; i < QuestionCount; i++)
            {
                var resolved = GetResolvedLabels(i);
                if (resolved.Count == 0)
                {
                    continue;
                }

                var question = Input.Questions[i];
                var header = string.IsNullOrEmpty(question.Header) ? $"Q{i + 1}" : question.Header;
                parts.Add($"{header}: {string.Join(", ", resolved)}");
            }

            return parts.Count > 0 ? string.Join("; ", parts) : "No selections made";
        }

        private AskUserQuestionItem GetQuestion(int questionIndex)
        {
            return questionIndex >= 0 && questionIndex < Input.Questions.Count
                ? Input.Questions[questionIndex]
                : null;
        }

        internal static bool IsOtherOption(AskUserQuestionOption option)
        {
            return option?.Label?.Equals("Other", StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
