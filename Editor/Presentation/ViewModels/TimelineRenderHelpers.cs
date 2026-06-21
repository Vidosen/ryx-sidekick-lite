// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Renderers;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    /// <summary>
    /// Static helpers for computing message element names and role headers in the chat timeline.
    /// Extracted from <c>SidekickWindow.UI.cs</c> for reuse by <c>ChatTimelineViewModel</c>.
    /// </summary>
    internal static class TimelineRenderHelpers
    {
        /// <summary>
        /// Returns the deterministic element name for a message (used as the UIElements element name
        /// for ListView slot identification).
        /// </summary>
        internal static string GetElementName(Message message)
        {
            if (message == null) return null;

            if (message.Role == MessageRole.Tool)
            {
                var toolId = message.ToolUses != null && message.ToolUses.Count > 0
                    ? message.ToolUses[0].Id
                    : null;
                return !string.IsNullOrEmpty(toolId) ? $"tool-{toolId}" : $"message-{message.Id}";
            }

            if (message.Role == MessageRole.Assistant && message.IsThinkingBlock)
            {
                return $"thinking-{message.Id}";
            }

            if (message.Role == MessageRole.User && MessageElementFactory.IsDomainReloadMessage(message.Content))
            {
                return $"banner-{message.Id}";
            }

            return $"message-{message.Id}";
        }

        /// <summary>
        /// Whether the message at <paramref name="index"/> should render its role header.
        /// Header is collapsed for tool / thinking-block messages and for consecutive messages
        /// of the same visible role. Index-based form callable from a <c>ListView</c> bindItem
        /// (equivalent to the sequential <c>ComputeShowHeader</c> the windowing path used).
        /// </summary>
        internal static bool ComputeShowRoleHeader(Conversation conversation, int index)
        {
            var messages = conversation?.Messages;
            if (messages == null || index < 0 || index >= messages.Count) return false;

            var msg = messages[index];
            if (msg == null) return false;
            if (msg.Role == MessageRole.Tool) return false;
            if (msg.Role == MessageRole.Assistant && msg.IsThinkingBlock) return false;

            var previousRole = GetPreviousVisibleRole(conversation, index - 1);
            return previousRole == null || previousRole != msg.Role;
        }

        /// <summary>
        /// Walks backwards from <paramref name="startFromIndex"/> to find the first non-tool,
        /// non-thinking-block message role (used to compute role-header collapsing).
        /// </summary>
        internal static MessageRole? GetPreviousVisibleRole(Conversation conversation, int startFromIndex)
        {
            if (conversation?.Messages == null) return null;

            for (int i = startFromIndex; i >= 0; i--)
            {
                var msg = conversation.Messages[i];
                if (msg == null) continue;
                if (msg.Role == MessageRole.Tool) continue;
                if (msg.Role == MessageRole.Assistant && msg.IsThinkingBlock) continue;
                return msg.Role;
            }

            return null;
        }
    }
}
