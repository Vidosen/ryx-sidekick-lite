// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Read-only conversation history surface for persistent-session providers
    /// that don't expose a separate <c>ICliHistoryProvider</c>. Implementations
    /// query the live session runtime rather than scanning JSONL files.
    /// </summary>
    internal interface ILiveConversationSessionBackend
    {
        Task<List<CliSessionInfo>> ListSessionsAsync(string workingDirectory);
        Task<(Conversation conversation, ConversationUsageInfo usage)> LoadConversationAsync(string sessionId, string workingDirectory);
    }
}
