// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Questions;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal sealed class ImplementPlanLocallyAdapter : IAskUserQuestionSchemaAdapter
    {
        private const string ImplementPlanPrefix = "PLEASE IMPLEMENT THIS PLAN:";
        private const string RequestMethod = "item/plan/requestImplementation";
        private const string OtherPlaceholder = "No, and tell Codex what to do differently";

        private readonly ISettingsStore _settingsStore;

        public ImplementPlanLocallyAdapter(ISettingsStore settingsStore = null)
        {
            _settingsStore = settingsStore;
        }

        public bool CanHandle(PendingPermission permission)
        {
            return permission is { IsLocalOnly: true, Kind: PendingPermissionKind.SessionUserInput }
                   && string.Equals(permission.RequestMethod, RequestMethod, StringComparison.Ordinal)
                   && ToolPresentationCatalog.GetEffectiveKind(permission) == ToolKind.ImplementPlan;
        }

        public AskUserQuestionPrompt BuildPrompt(PendingPermission permission)
        {
            return new AskUserQuestionPrompt(new AskUserQuestionInput
            {
                Questions = new System.Collections.Generic.List<AskUserQuestionItem>
                {
                    new AskUserQuestionItem
                    {
                        Header = "Implement this plan?",
                        Question = "Review the plan above and choose how to proceed",
                        MultiSelect = false,
                        IsOther = true,
                        OtherPlaceholder = OtherPlaceholder,
                        Options = new System.Collections.Generic.List<AskUserQuestionOption>
                        {
                            new AskUserQuestionOption
                            {
                                Label = "Yes, implement this plan"
                            }
                        }
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
            var feedback = state?.GetOtherText(0)?.Trim();

            if (selectedIndex == 0)
            {
                return AskUserQuestionDispatch.ForLocalAction(
                    localFollowupText: BuildImplementationPrompt(permission),
                    modeTransition: new AskUserQuestionModeTransition
                    {
                        CollaborationMode = SidekickAppConstants.CollaborationModes.Default,
                        PermissionMode = _settingsStore?.PermissionMode
                    });
            }

            if (!string.IsNullOrWhiteSpace(feedback))
            {
                return AskUserQuestionDispatch.ForLocalAction(localFollowupText: feedback);
            }

            return BuildCancel(permission, prompt);
        }

        public AskUserQuestionDispatch BuildCancel(PendingPermission permission, AskUserQuestionPrompt prompt)
        {
            return AskUserQuestionDispatch.ForLocalAction();
        }

        private static string BuildImplementationPrompt(PendingPermission permission)
        {
            var plan = permission?.Input?["plan"]?.ToString()?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(plan)
                ? ImplementPlanPrefix
                : $"{ImplementPlanPrefix}\n{plan}";
        }
    }
}
