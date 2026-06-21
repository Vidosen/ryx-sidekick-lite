// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Ryx.Sidekick.Editor.Domain.Models
{
    /// <summary>
    /// Provider-specific request kind for routing the permission reply channel.
    /// </summary>
    internal enum PendingPermissionKind
    {
        LegacyToolPermission,
        ClaudeControlRequest,
        SessionCommandApproval,
        SessionFileApproval,
        SessionUserInput
    }

    /// <summary>
    /// Categorisation of an individual session-runtime permission option.
    /// </summary>
    internal enum SessionPermissionOptionKind
    {
        Unknown,
        AllowOnce,
        AllowAlways,
        RejectOnce,
        RejectAlways,
        Cancelled
    }

    /// <summary>
    /// One selectable permission option supplied by a session runtime.
    /// </summary>
    [Serializable]
    internal sealed class SessionPermissionOption
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public SessionPermissionOptionKind Kind { get; set; }
    }

    /// <summary>
    /// Represents a pending permission request shown to the user.
    /// </summary>
    internal class PendingPermission
    {
        public string ToolUseId { get; set; }
        public string ProviderId { get; set; }
        public ToolKind ToolKind { get; set; }
        public string ToolName { get; set; }
        public string RawToolName { get; set; }
        public string RawToolTitle { get; set; }
        public string DecisionKey { get; set; }
        public string FilePath { get; set; }
        public string Command { get; set; }
        public JToken Input { get; set; }
        public string SessionId { get; set; }
        public string RawInput { get; set; }

        /// <summary>Request ID for control_request/control_response flow.</summary>
        public string RequestId { get; set; }

        /// <summary>Raw JSON-RPC request ID token for session runtime replies.</summary>
        public JToken RequestIdToken { get; set; }

        /// <summary>Agent ID from the control_request (for multi-agent scenarios).</summary>
        public string AgentId { get; set; }

        /// <summary>Permission suggestions from CLI.</summary>
        public JToken Suggestions { get; set; }

        /// <summary>Blocked path that triggered the request.</summary>
        public string BlockedPath { get; set; }

        /// <summary>Reason for the permission decision.</summary>
        public string DecisionReason { get; set; }

        /// <summary>Whether this permission came from a control_request (requires control_response).</summary>
        public bool IsControlRequest { get; set; }

        /// <summary>Provider-specific request kind used to route the reply channel.</summary>
        public PendingPermissionKind Kind { get; set; }

        /// <summary>Session runtime permission options, if the provider supplied them.</summary>
        public List<SessionPermissionOption> Options { get; set; } = new();

        /// <summary>Provider-specific cache key for remember/auto-decision flows.</summary>
        public string CacheKey { get; set; }

        /// <summary>Option selected automatically or by the UI when responding.</summary>
        public string SelectedOptionId { get; set; }

        /// <summary>Original provider request method for session-based runtimes.</summary>
        public string RequestMethod { get; set; }

        /// <summary>True when the permission exists only in local UI and should not answer the provider.</summary>
        public bool IsLocalOnly { get; set; }
    }
}
