// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Read-side view of the currently-active conversation that chat use cases
    /// consume. Implementations live in the controller layer and project the
    /// long-lived <see cref="Conversation"/> aggregate that the window currently
    /// drives.
    /// </summary>
    internal interface IChatConversationSession
    {
        event Action Changed;

        Conversation CurrentConversation { get; }

        bool IsCurrentConversationLoading { get; }

        (Conversation conversation, bool created) EnsureConversation();
    }
}
