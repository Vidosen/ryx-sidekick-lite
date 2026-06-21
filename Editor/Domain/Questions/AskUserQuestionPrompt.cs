// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Domain.Questions
{
    internal sealed class AskUserQuestionPrompt
    {
        public AskUserQuestionPrompt(AskUserQuestionInput input)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public AskUserQuestionInput Input { get; }
    }
}
