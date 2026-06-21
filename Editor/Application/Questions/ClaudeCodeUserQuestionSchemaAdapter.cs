// SPDX-License-Identifier: GPL-3.0-only
using System.Linq;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Questions;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal sealed class ClaudeCodeUserQuestionSchemaAdapter : IAskUserQuestionSchemaAdapter
    {
        private const string ContinuePlanningMessage = "User chose to stay in plan mode and continue planning";
        private const string RejectionReasonPrefix =
            "The user doesn't want to proceed with this tool use. The tool use was rejected (eg. if it was a file edit, the new_string was NOT written to the file). The user provided the following reason for the rejection: ";

        private readonly ISettingsStore _settingsStore;
        private readonly IProviderCatalog _providerCatalog;

        public ClaudeCodeUserQuestionSchemaAdapter(
            ISettingsStore settingsStore = null,
            IProviderCatalog providerCatalog = null)
        {
            _settingsStore = settingsStore;
            _providerCatalog = providerCatalog;
        }

        public bool CanHandle(PendingPermission permission)
        {
            return permission != null
                && ToolPresentationCatalog.GetEffectiveKind(permission) == ToolKind.ExitPlanMode
                && !AcpExitPlanModeAdapter.IsAcpRequest(permission);
        }

        public AskUserQuestionPrompt BuildPrompt(PendingPermission permission)
        {
            return new AskUserQuestionPrompt(CreateExitPlanModeInput(permission));
        }

        public AskUserQuestionDispatch BuildSubmit(
            PendingPermission permission,
            AskUserQuestionPrompt prompt,
            AskUserQuestionSessionState state)
        {
            var selectedIndex = state?.GetSelectedIndices(0).Count > 0
                ? state.GetSelectedIndices(0).First()
                : -1;
            var customText = selectedIndex == 3 ? state?.GetOtherText(0)?.Trim() ?? string.Empty : string.Empty;

            var currentPermissionMode = _settingsStore?.PermissionMode;
            var activeProvider = ResolveActiveProviderMetadata();

            bool allow;
            string message;
            AskUserQuestionModeTransition transition = null;

            switch (selectedIndex)
            {
                case 0:
                    allow = true;
                    message = "yes_auto";
                    transition = ExitPlanModeTransitionResolver.Create(autoApprove: true, currentPermissionMode, activeProvider);
                    break;

                case 1:
                    allow = true;
                    message = "yes_manual";
                    transition = ExitPlanModeTransitionResolver.Create(autoApprove: false, currentPermissionMode, activeProvider);
                    break;

                case 2:
                    return BuildContinuePlanningDispatch(permission, ContinuePlanningMessage);

                case 3:
                    if (string.IsNullOrWhiteSpace(customText))
                    {
                        return BuildCancel(permission, prompt);
                    }

                    return BuildContinuePlanningDispatch(permission, RejectionReasonPrefix + customText);

                default:
                    return BuildCancel(permission, prompt);
            }

            if (permission?.Kind == PendingPermissionKind.SessionUserInput)
            {
                return AskUserQuestionDispatch.ForUserInputResponse(new JObject
                {
                    ["decision"] = allow ? "accept" : "continue_planning",
                    ["message"] = message,
                    ["collaborationMode"] = transition?.CollaborationMode ?? SidekickAppConstants.CollaborationModes.Plan,
                    ["permissionMode"] = transition?.PermissionMode ?? currentPermissionMode
                }, transition);
            }

            return AskUserQuestionDispatch.ForControlResponse(
                allow: allow,
                updatedInput: null,
                message: message,
                modeTransition: transition);
        }

        private IProviderUiMetadata ResolveActiveProviderMetadata()
        {
            if (_providerCatalog == null || _settingsStore == null)
            {
                return null;
            }

            var providerId = _settingsStore.ProviderId;
            var module = _providerCatalog.GetProvider(providerId);
            return module?.Metadata;
        }

        public AskUserQuestionDispatch BuildCancel(PendingPermission permission, AskUserQuestionPrompt prompt)
        {
            if (permission?.Kind == PendingPermissionKind.SessionUserInput)
            {
                return AskUserQuestionDispatch.ForUserInputResponse(new JObject
                {
                    ["decision"] = "cancelled",
                    ["message"] = "User cancelled the question dialog"
                });
            }

            return AskUserQuestionDispatch.ForControlResponse(
                allow: false,
                updatedInput: null,
                message: "User cancelled the question dialog");
        }

        private AskUserQuestionDispatch BuildContinuePlanningDispatch(PendingPermission permission, string message)
        {
            if (permission?.Kind == PendingPermissionKind.SessionUserInput)
            {
                return AskUserQuestionDispatch.ForUserInputResponse(new JObject
                {
                    ["decision"] = "continue_planning",
                    ["message"] = message ?? string.Empty,
                    ["collaborationMode"] = SidekickAppConstants.CollaborationModes.Plan,
                    ["permissionMode"] = _settingsStore?.PermissionMode
                });
            }

            return AskUserQuestionDispatch.ForControlResponse(
                allow: false,
                updatedInput: null,
                message: message);
        }

        private static AskUserQuestionInput CreateExitPlanModeInput(PendingPermission permission)
        {
            if (permission?.Options is { Count: > 0 })
            {
                return new AskUserQuestionInput
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
                };
            }

            return new AskUserQuestionInput
            {
                Questions = new System.Collections.Generic.List<AskUserQuestionItem>
                {
                    new AskUserQuestionItem
                    {
                        Header = "Accept this plan?",
                        Question = "Review the plan above and decide whether to proceed",
                        MultiSelect = false,
                        Options = new System.Collections.Generic.List<AskUserQuestionOption>
                        {
                            new AskUserQuestionOption { Label = "Yes, and auto-accept" },
                            new AskUserQuestionOption { Label = "Yes, and manually approve edits" },
                            new AskUserQuestionOption { Label = "No, keep planning" },
                            new AskUserQuestionOption { Label = "Other" }
                        }
                    }
                }
            };
        }
    }
}
