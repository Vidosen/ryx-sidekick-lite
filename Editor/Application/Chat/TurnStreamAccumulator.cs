// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Text;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Chat
{
    internal sealed class TurnStreamAccumulator
    {
        public Dictionary<string, ToolUse> ActiveTools { get; } = new();

        public Dictionary<string, Message> ToolMessages { get; } = new();

        public StringBuilder StreamingContent { get; } = new();

        public StringBuilder ThinkingContent { get; } = new();

        public Message CurrentStreamingMessage { get; set; }

        public Message CurrentThinkingMessage { get; set; }

        public bool IsThinkingActive { get; set; }

        public DateTime ThinkingStartTime { get; set; }

        public void ClearTurnBuffers()
        {
            StreamingContent.Clear();
            ThinkingContent.Clear();
            IsThinkingActive = false;
            CurrentThinkingMessage = null;
        }

        public void ResetAfterStreamComplete()
        {
            CurrentStreamingMessage = null;
            CurrentThinkingMessage = null;
            StreamingContent.Clear();
            ThinkingContent.Clear();
            ActiveTools.Clear();
            ToolMessages.Clear();
            IsThinkingActive = false;
            ThinkingStartTime = default;
        }
    }
}
