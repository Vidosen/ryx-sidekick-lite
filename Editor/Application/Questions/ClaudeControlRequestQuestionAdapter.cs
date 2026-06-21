// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Questions;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal sealed class ClaudeControlRequestQuestionAdapter : IAskUserQuestionSchemaAdapter
    {
        public bool CanHandle(PendingPermission permission)
        {
            return permission != null
                && permission.Kind != PendingPermissionKind.SessionUserInput
                && ToolPresentationCatalog.GetEffectiveKind(permission) == ToolKind.AskUserQuestion;
        }

        public AskUserQuestionPrompt BuildPrompt(PendingPermission permission)
        {
            var input = AskUserQuestionInput.FromJToken(permission?.Input);
            return input == null ? null : new AskUserQuestionPrompt(input);
        }

        public AskUserQuestionDispatch BuildSubmit(
            PendingPermission permission,
            AskUserQuestionPrompt prompt,
            AskUserQuestionSessionState state)
        {
            return AskUserQuestionDispatch.ForControlResponse(
                allow: true,
                updatedInput: BuildUpdatedInput(prompt?.Input, state));
        }

        public AskUserQuestionDispatch BuildCancel(PendingPermission permission, AskUserQuestionPrompt prompt)
        {
            return AskUserQuestionDispatch.ForControlResponse(
                allow: false,
                updatedInput: null,
                message: "User cancelled the question dialog");
        }

        private static JObject BuildUpdatedInput(AskUserQuestionInput input, AskUserQuestionSessionState state)
        {
            var answers = new JObject();

            if (input != null)
            {
                for (int i = 0; i < input.Questions.Count; i++)
                {
                    var resolved = state?.GetResolvedLabels(i) ?? new List<string>();
                    if (resolved.Count == 0)
                    {
                        continue;
                    }

                    var question = input.Questions[i];
                    answers[question.Question ?? $"Q{i + 1}"] = string.Join(", ", resolved);
                }
            }

            var questionsArray = input?.Questions != null
                ? JArray.FromObject(input.Questions.Select(question => new
                {
                    header = question.Header,
                    question = question.Question,
                    options = question.Options?.Select(option => new { label = option.Label, description = option.Description }),
                    multiSelect = question.MultiSelect
                }))
                : new JArray();

            return new JObject
            {
                ["questions"] = questionsArray,
                ["answers"] = answers
            };
        }
    }
}
