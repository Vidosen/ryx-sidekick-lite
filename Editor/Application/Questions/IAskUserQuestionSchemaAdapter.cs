// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Questions;

namespace Ryx.Sidekick.Editor.UseCases.Questions
{
    internal interface IAskUserQuestionSchemaAdapter
    {
        bool CanHandle(PendingPermission permission);

        AskUserQuestionPrompt BuildPrompt(PendingPermission permission);

        AskUserQuestionDispatch BuildSubmit(
            PendingPermission permission,
            AskUserQuestionPrompt prompt,
            AskUserQuestionSessionState state);

        AskUserQuestionDispatch BuildCancel(PendingPermission permission, AskUserQuestionPrompt prompt);
    }
}
