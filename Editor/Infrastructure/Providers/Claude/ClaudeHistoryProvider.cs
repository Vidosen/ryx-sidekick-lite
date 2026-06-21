// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Providers.Claude
{
    /// <summary>
    /// History provider for Claude Code CLI.
    /// Delegates to the static CliHistoryService.
    /// </summary>
    internal class ClaudeHistoryProvider : ICliHistoryProvider
    {
        private readonly IProviderToolMapper _toolMapper;

        public ClaudeHistoryProvider(IProviderToolMapper toolMapper = null)
        {
            _toolMapper = toolMapper ?? new ClaudeCliProvider().CreateToolMapper();
        }

        public string GetStoragePath()
        {
            return CliHistoryService.GetCliStoragePath();
        }

        public List<CliSessionInfo> ListSessions()
        {
            return CliHistoryService.ListSessions();
        }

        public Conversation LoadConversation(string sessionId)
        {
            var conversation = CliHistoryService.LoadConversation(sessionId);
            _toolMapper?.Normalize(conversation);
            return conversation;
        }

        public ConversationUsageInfo GetSessionUsage(string sessionId)
        {
            return CliHistoryService.GetSessionUsage(sessionId);
        }
    }
}
