// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Reads conversation history from a provider-specific storage location.
    /// </summary>
    internal interface ICliHistoryProvider
    {
        /// <summary>Returns the root directory where this provider stores sessions.</summary>
        string GetStoragePath();
        System.Collections.Generic.List<CliSessionInfo> ListSessions();
        Conversation LoadConversation(string sessionId);
        ConversationUsageInfo GetSessionUsage(string sessionId);
    }
}
