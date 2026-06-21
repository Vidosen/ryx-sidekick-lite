// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Questions;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal sealed class SessionAskQuestionAdapter : IAskUserQuestionSchemaAdapter
    {
        public bool CanHandle(PendingPermission permission)
        {
            return permission != null
                && permission.Kind == PendingPermissionKind.SessionUserInput
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
            var answers = new JObject();
            var input = prompt?.Input;

            if (input != null)
            {
                for (int i = 0; i < input.Questions.Count; i++)
                {
                    var question = input.Questions[i];
                    if (string.IsNullOrEmpty(question?.Id))
                    {
                        continue;
                    }

                    if (state != null && !state.IsQuestionAnswered(i))
                    {
                        continue;
                    }

                    var resolved = state?.GetResolvedLabels(i);
                    if (resolved == null || resolved.Count == 0)
                    {
                        continue;
                    }

                    answers[question.Id] = new JObject
                    {
                        ["answers"] = new JArray(resolved.ToArray())
                    };
                }
            }

            return AskUserQuestionDispatch.ForUserInputResponse(new JObject
            {
                ["answers"] = answers
            });
        }

        public AskUserQuestionDispatch BuildCancel(PendingPermission permission, AskUserQuestionPrompt prompt)
        {
            return AskUserQuestionDispatch.ForUserInputResponse(new JObject
            {
                ["answers"] = new JObject()
            });
        }
    }
}
