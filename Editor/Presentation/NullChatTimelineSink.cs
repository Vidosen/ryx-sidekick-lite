// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    internal sealed class NullChatTimelineSink : IChatTimelineSink
    {
        public static readonly NullChatTimelineSink Instance = new();

        private NullChatTimelineSink() { }

        public void AppendMessage(Message message) { }
        public void AppendToolMessage(Message toolMessage) { }
        public void UpdateStreamingMessage() { }
        public void UpdateToolMessage(string toolUseId) { }
        public void AppendThinkingMessage(Message thinkingMessage) { }
        public void UpdateThinkingMessage(string toolUseId) { }
        public void Refresh() { }
    }
}
