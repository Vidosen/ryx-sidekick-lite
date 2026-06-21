// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor
{
    internal interface IConversationRepository
    {
        Task<List<CliSessionInfo>> ListSessionsAsync();
        Task<Conversation> LoadConversationAsync(string sessionId);
        Task<ConversationUsageInfo> GetSessionUsageAsync(string sessionId);
    }
}
