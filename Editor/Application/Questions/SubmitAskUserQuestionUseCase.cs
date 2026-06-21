// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Questions;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal enum AskUserQuestionToggleAction { None, Submit, AdvanceTab }

    internal readonly struct AskUserQuestionToggleOutcome
    {
        public readonly AskUserQuestionToggleAction Action;
        public readonly int NextTabIndex;

        private AskUserQuestionToggleOutcome(AskUserQuestionToggleAction action, int nextTabIndex)
        {
            Action = action;
            NextTabIndex = nextTabIndex;
        }

        public static AskUserQuestionToggleOutcome None() =>
            new AskUserQuestionToggleOutcome(AskUserQuestionToggleAction.None, -1);

        public static AskUserQuestionToggleOutcome SubmitImmediately() =>
            new AskUserQuestionToggleOutcome(AskUserQuestionToggleAction.Submit, -1);

        public static AskUserQuestionToggleOutcome AdvanceTo(int tabIndex) =>
            new AskUserQuestionToggleOutcome(AskUserQuestionToggleAction.AdvanceTab, tabIndex);
    }

    internal sealed class SubmitAskUserQuestionUseCase
    {
        private readonly AskUserQuestionSchemaRegistry _schemaRegistry;

        public SubmitAskUserQuestionUseCase(AskUserQuestionSchemaRegistry schemaRegistry = null)
        {
            _schemaRegistry = schemaRegistry ?? AskUserQuestionSchemaRegistry.CreateDefault();
        }

        public AskUserQuestionDispatch Submit(
            PendingPermission permission,
            AskUserQuestionPrompt prompt,
            AskUserQuestionSessionState state)
        {
            if (permission == null || prompt?.Input?.Questions == null || prompt.Input.Questions.Count == 0)
            {
                return null;
            }

            var adapter = _schemaRegistry.Resolve(permission);
            if (adapter == null)
            {
                return null;
            }

            return adapter.BuildSubmit(permission, prompt, state);
        }

        public AskUserQuestionDispatch Cancel(PendingPermission permission, AskUserQuestionPrompt prompt)
        {
            if (permission == null || prompt?.Input == null)
            {
                return null;
            }

            var adapter = _schemaRegistry.Resolve(permission);
            if (adapter == null)
            {
                return null;
            }

            return adapter.BuildCancel(permission, prompt);
        }

        /// <summary>
        /// Pure decision: given the current toggle context, return the auto-action to take after
        /// a single-select option is toggled. Does not mutate any state.
        /// </summary>
        public AskUserQuestionToggleOutcome DecideAfterToggle(
            AskUserQuestionInput input,
            AskUserQuestionSessionState state,
            int questionIndex,
            int optionIndex,
            int activeTabIndex,
            bool isMultiSelect)
        {
            if (input == null || state == null)
            {
                return AskUserQuestionToggleOutcome.None();
            }

            if (questionIndex < 0 || questionIndex >= input.Questions.Count)
            {
                return AskUserQuestionToggleOutcome.None();
            }

            var question = input.Questions[questionIndex];

            if (optionIndex < 0 || optionIndex >= question.Options.Count)
            {
                return AskUserQuestionToggleOutcome.None();
            }

            if (!isMultiSelect && input.Questions.Count == 1)
            {
                var option = question.Options[optionIndex];
                if (!question.IsOther && !AskUserQuestionSessionState.IsOtherOption(option))
                {
                    return AskUserQuestionToggleOutcome.SubmitImmediately();
                }

                return AskUserQuestionToggleOutcome.None();
            }

            if (!isMultiSelect && input.Questions.Count > 1)
            {
                var option = question.Options[optionIndex];
                if (!AskUserQuestionSessionState.IsOtherOption(option) && state.IsQuestionAnswered(questionIndex))
                {
                    var nextUnanswered = state.FindNextUnansweredQuestion(questionIndex);
                    if (nextUnanswered != -1 && nextUnanswered != activeTabIndex)
                    {
                        return AskUserQuestionToggleOutcome.AdvanceTo(nextUnanswered);
                    }
                }
            }

            return AskUserQuestionToggleOutcome.None();
        }
    }
}
