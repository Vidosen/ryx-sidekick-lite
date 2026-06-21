// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace Ryx.Sidekick.Editor.Domain.Models
{
    /// <summary>
    /// In-memory snapshot of the composer draft for a provider scope, captured by
    /// the shell before switching providers so it can be restored when the user
    /// returns to the same provider.
    /// </summary>
    [Serializable]
    internal sealed class ProviderDraftSnapshot
    {
        public string SelectedSessionId;
        public string DraftText;
        public List<SerializedContextAttachment> DraftContextAttachments = new();
        public List<ImageAttachment> DraftImageAttachments = new();
    }

    /// <summary>
    /// Persisted per-provider UI state — model, mode, last session, etc. — that
    /// <see cref="ISettingsStore"/>-style adapters can round-trip across sessions.
    /// </summary>
    [Serializable]
    internal sealed class ProviderUiStateSnapshot
    {
        public string ProviderId;
        public string SelectedSessionId;
        public string Model;
        public string ReasoningEffort;
        public string CollaborationMode;
        public string PermissionMode;
        public bool EnableThinking;
        public int MaxThinkingTokens = 16000;
    }
}
