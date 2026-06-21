// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor
{
    internal interface ISettingsStore
    {
        event Action<ActiveProviderStateSnapshot> ActiveProviderStateChanged;

        string ProviderId { get; set; }
        string Model { get; set; }
        string ReasoningEffort
        {
            get => string.Empty;
            set { }
        }
        string CollaborationMode { get; set; }
        string PermissionMode { get; set; }
        string LastOpenedSessionId { get; set; }
        string WorkingDirectory { get; }
        bool VerboseLogging { get; }
        bool DebugMode { get; }
        bool EnableThinking { get; set; }
        int MaxThinkingTokens { get; }
        bool AutoConnectMcpForUnity { get; }
        bool AutoStartMcpForUnityServer { get; }
        ActiveProviderStateSnapshot CurrentActiveProviderState { get; }
        string GetCliPath();
        (bool success, string message) ValidateCli();
        ProviderUiStateSnapshot GetProviderUiState(string providerId);
        void SaveProviderUiState(ProviderUiStateSnapshot snapshot);
        ProviderModelCatalog GetModelCatalog(string providerId) => null;
        void SaveModelCatalog(ProviderModelCatalog catalog) { }

        /// <summary>
        /// Builds CLI invocation arguments for the active provider using current settings.
        /// </summary>
        string BuildArguments(
            string prompt = null,
            bool printMode = true,
            bool continueSession = false,
            string sessionId = null,
            PromptTransportMode promptTransportMode = PromptTransportMode.Argument,
            bool includePrompt = true,
            IReadOnlyList<string> imageAttachmentPaths = null);

        /// <summary>
        /// Creates a ProcessStartInfo configured for the active provider's CLI.
        /// </summary>
        ProcessStartInfo CreateProcessStartInfo(string arguments);
    }
}
