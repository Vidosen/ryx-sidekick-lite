// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Parameter object passed to <see cref="ICliProvider.BuildArguments"/>.
    /// </summary>
    internal class CliArgumentContext
    {
        public string Prompt { get; set; }
        public bool PrintMode { get; set; } = true;
        public bool ContinueSession { get; set; }
        public string SessionId { get; set; }
        public PromptTransportMode PromptTransportMode { get; set; }
        public bool IncludePrompt { get; set; } = true;
        public string Model { get; set; }
        public string ReasoningEffort { get; set; }
        public string CollaborationMode { get; set; }
        public string PermissionMode { get; set; }
        public int MaxTurns { get; set; }
        public bool EnableThinking { get; set; }
        public int MaxThinkingTokens { get; set; }
        public string McpConfigArgs { get; set; }
        public string WorkingDirectory { get; set; }
        public IReadOnlyList<string> ImageAttachmentPaths { get; set; }
    }
}
