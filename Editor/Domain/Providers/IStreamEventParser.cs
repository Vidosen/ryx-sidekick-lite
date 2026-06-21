// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Parses a provider-specific stream-json output line and dispatches typed events.
    /// </summary>
    internal interface IStreamEventParser
    {
        event Action<StreamEvent> OnStreamEvent;
        event Action<string> OnTextDelta;
        event Action<ToolUse> OnToolUse;
        event Action<string, string> OnToolResult;
        event Action<PendingPermission> OnPermissionRequest;
        event Action<ImageAttachment> OnImageAttachment;
        event Action<string> OnControlRequest;
        event Action<string> OnSessionIdReceived;
        event Action<ResultEvent> OnResult;
        event Action<string> OnRawLine;

        event Action OnThinkingStarted;
        event Action<string> OnThinkingDelta;
        event Action<string> OnThinkingCompleted;

        bool IsThinkingActive { get; }
        double ThinkingElapsedSeconds { get; }
        ToolUseTracker ToolTracker { get; }

        /// <summary>Processes one line of CLI stdout.</summary>
        void ProcessLine(string line);

        /// <summary>Resets accumulated state for a new turn.</summary>
        void Reset();
    }
}
