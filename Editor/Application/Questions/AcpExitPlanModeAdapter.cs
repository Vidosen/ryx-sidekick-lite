// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Questions;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal sealed class AcpExitPlanModeAdapter : IAskUserQuestionSchemaAdapter
    {
        public bool CanHandle(PendingPermission permission)
        {
            return IsAcpRequest(permission);
        }

        public AskUserQuestionPrompt BuildPrompt(PendingPermission permission)
        {
            return new AskUserQuestionPrompt(new AskUserQuestionInput
            {
                Questions = new System.Collections.Generic.List<AskUserQuestionItem>
                {
                    new AskUserQuestionItem
                    {
                        Header = "Accept this plan?",
                        Question = "Review the plan above and choose how to proceed",
                        MultiSelect = false,
                        Options = permission.Options.Select(option => new AskUserQuestionOption
                        {
                            Label = option.Label ?? option.Id ?? "Option",
                            Description = option.Description
                        }).ToList()
                    }
                }
            });
        }

        public AskUserQuestionDispatch BuildSubmit(
            PendingPermission permission,
            AskUserQuestionPrompt prompt,
            AskUserQuestionSessionState state)
        {
            var selectedIndex = state?.GetSelectedIndices(0).Count > 0
                ? state.GetSelectedIndices(0).First()
                : -1;
            var selectedOption = selectedIndex >= 0
                && selectedIndex < (permission?.Options?.Count ?? 0)
                ? permission.Options[selectedIndex]
                : null;

            return AskUserQuestionDispatch.ForUserInputResponse(selectedOption == null || string.IsNullOrWhiteSpace(selectedOption.Id)
                ? BuildCancelledOutcomeResponse()
                : new JObject
                {
                    ["outcome"] = new JObject
                    {
                        ["outcome"] = "selected",
                        ["optionId"] = selectedOption.Id
                    }
                });
        }

        public AskUserQuestionDispatch BuildCancel(PendingPermission permission, AskUserQuestionPrompt prompt)
        {
            return AskUserQuestionDispatch.ForUserInputResponse(BuildCancelledOutcomeResponse());
        }

        public static bool IsAcpRequest(PendingPermission permission)
        {
            return permission is { Kind: PendingPermissionKind.SessionUserInput }
                   && ToolPresentationCatalog.GetEffectiveKind(permission) == ToolKind.ExitPlanMode
                   && string.Equals(permission.RequestMethod, "session/request_permission", System.StringComparison.Ordinal)
                   && permission.Options is { Count: > 0 };
        }

        private static JObject BuildCancelledOutcomeResponse()
        {
            return new JObject
            {
                ["outcome"] = new JObject
                {
                    ["outcome"] = "cancelled"
                }
            };
        }
    }
}
