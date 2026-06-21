// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Conversations
{
    internal enum ConversationLoadNextAction
    {
        None,
        LoadSelectedHistory,
        EnsureEmptyConversation
    }

    internal sealed class ConversationUsagePayload
    {
        public static ConversationUsagePayload None()
        {
            return new ConversationUsagePayload
            {
                HasUsage = false,
                TotalTokens = 0,
                ContextWindow = 0
            };
        }

        public static ConversationUsagePayload FromUsage(ConversationUsageInfo usage)
        {
            if (usage == null)
            {
                return None();
            }

            return new ConversationUsagePayload
            {
                HasUsage = true,
                TotalTokens = usage.TotalTokens,
                ContextWindow = usage.ContextWindow
            };
        }

        public bool HasUsage { get; set; }
        public int TotalTokens { get; set; }
        public int ContextWindow { get; set; }
    }

    internal sealed class ConversationLoadProgress
    {
        public ConversationLoadStatus<ConversationListLoadState> ListStatus { get; set; }
        public ConversationLoadStatus<ConversationHistoryLoadState> HistoryStatus { get; set; }
        public ConversationUsagePayload Usage { get; set; }
        public bool ShouldRefreshUi { get; set; }
    }
}
