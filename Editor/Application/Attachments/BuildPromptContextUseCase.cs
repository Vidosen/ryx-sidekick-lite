// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Text;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Attachments
{
    /// <summary>
    /// Assembles the textual portion of a CLI prompt: context XML lines followed by the
    /// user prompt. Preserves the XML wire format the CLIs and our serializers expect.
    /// Stateless — safe to register as a singleton.
    /// </summary>
    internal sealed class BuildPromptContextUseCase
    {
        /// <summary>
        /// Builds the plain-text prompt string that includes any context XML tags prepended
        /// before the user's prompt text.
        /// </summary>
        /// <param name="prompt">The user prompt. May be null or empty.</param>
        /// <param name="contextAttachments">Optional list of context attachments. Null is treated as empty.</param>
        /// <returns>
        /// A string containing zero or more <c>&lt;context_*&gt;</c> XML lines (each followed by a
        /// newline), an optional blank separator line when both context and prompt are present,
        /// and then the prompt text. Returns an empty string when both inputs are empty/null.
        /// </returns>
        public string Execute(string prompt, IReadOnlyList<IContextAttachment> contextAttachments)
        {
            var textContent = new StringBuilder();

            if (contextAttachments is { Count: > 0 })
            {
                foreach (var ctx in contextAttachments)
                {
                    var contextXml = ctx.ToContextXml();
                    if (!string.IsNullOrEmpty(contextXml))
                    {
                        textContent.AppendLine(contextXml);
                    }
                }

                if (!string.IsNullOrEmpty(prompt))
                {
                    textContent.AppendLine();
                }
            }

            if (!string.IsNullOrEmpty(prompt))
            {
                textContent.Append(prompt);
            }

            return textContent.ToString();
        }
    }
}
