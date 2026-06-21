// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Presentation.Contracts
{
    internal interface IChatTimelineSink
    {
        void AppendMessage(Message message);
        void AppendToolMessage(Message toolMessage);
        void UpdateStreamingMessage();
        void UpdateToolMessage(string toolUseId);
        void AppendThinkingMessage(Message thinkingMessage);
        void UpdateThinkingMessage(string toolUseId);
        void Refresh();
    }
}
