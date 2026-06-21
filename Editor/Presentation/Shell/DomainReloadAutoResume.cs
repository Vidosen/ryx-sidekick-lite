// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Handles automatic resumption of Claude agent sessions after Unity domain reload.
    /// Persists active turn state via SessionState and triggers resume after reload completes.
    /// Also handles input field state persistence across domain reload and window close.
    /// </summary>
    [InitializeOnLoad]
    internal static class DomainReloadAutoResume
    {
        private const double HostRestoreTimeoutSeconds = 30d;
        private static readonly IResumeStateStore ResumeStateStore = new SessionStateResumeStateStore();
        private static readonly IEditorScheduler Scheduler = new UnityEditorScheduler();
        private static bool _isQuitting;
        private static double _hostRestoreWaitStartedAt;

        static DomainReloadAutoResume()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.quitting += OnEditorQuitting;
        }

        private static void OnEditorQuitting()
        {
            _isQuitting = true;
            ResumeStateStore.ClearPendingResume();
            ResumeStateStore.SaveInputFieldState(null);
        }

        private static void OnBeforeAssemblyReload()
        {
            if (_isQuitting) return;

            foreach (var host in SidekickWindowHostRegistry.Snapshot())
            {
                SaveInputFieldState(host);

                if (!host.IsTurnActive) continue;

                var sessionId = host.CurrentSessionId;
                if (string.IsNullOrEmpty(sessionId)) continue;
                var providerId = host.CurrentProviderId;
                var hostToken = host.HostToken;

                ResumeStateStore.SavePendingResume(hostToken, providerId, sessionId);

                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[Ryx Sidekick] Domain reload detected during active turn. Scheduled auto-resume for provider={providerId} session={sessionId} host={hostToken}");
                }

                // Only need to track one active session
                break;
            }
        }

        private static void OnAfterAssemblyReload()
        {
            if (!HasPendingResume()) return;

            // Start polling until editor is stable
            _hostRestoreWaitStartedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += WaitForStableEditor;
        }

        private static void WaitForStableEditor()
        {
            // Wait until Unity is done compiling and updating
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            // Stop polling
            EditorApplication.update -= WaitForStableEditor;

            if (!ResumeStateStore.TryConsumePendingResume(out var hostToken, out var providerId, out var sessionId))
            {
                Debug.LogWarning("[Ryx Sidekick] Auto-resume triggered but no pending state was found");
                return;
            }

            if (!SidekickWindowHostRegistry.TryFindByHostToken(hostToken, out var targetHost))
            {
                if (EditorApplication.timeSinceStartup - _hostRestoreWaitStartedAt < HostRestoreTimeoutSeconds)
                {
                    if (SidekickSettings.instance.VerboseLogging)
                    {
                        Debug.Log($"[Ryx Sidekick] Auto-resume waiting: host {hostToken} has not been restored yet");
                    }

                    ResumeStateStore.SavePendingResume(hostToken, providerId, sessionId);
                    EditorApplication.update += WaitForStableEditor;
                    return;
                }

                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[Ryx Sidekick] Auto-resume skipped: host {hostToken} was not restored");
                }
                return;
            }

            // Use delayCall to ensure UI is fully initialized
            Scheduler.Schedule(() => targetHost.AutoResumeAfterDomainReload(providerId, sessionId));
        }

        private static bool HasPendingResume()
        {
            return ResumeStateStore.TryConsumePendingResume(out var hostToken, out var providerId, out var sessionId)
                   && !string.IsNullOrEmpty(sessionId)
                   && RestorePendingResume(hostToken, providerId, sessionId);
        }

        private static bool RestorePendingResume(string hostToken, string providerId, string sessionId)
        {
            ResumeStateStore.SavePendingResume(hostToken, providerId, sessionId);
            return true;
        }

        #region Input Field State Persistence

        /// <summary>
        /// Saves input field state to SessionState for persistence across domain reload or window close.
        /// Called from OnBeforeAssemblyReload and SidekickWindow.OnDisable.
        /// </summary>
        internal static void SaveInputFieldState(ISidekickWindowHost host)
        {
            if (host == null) return;
            if (_isQuitting) return;

            var state = host.CaptureInputFieldState();
            if (state == null) return;

            // Only save if there's something to persist
            var hasText = !string.IsNullOrEmpty(state.InputText);
            var hasContext = state.ContextAttachments != null && state.ContextAttachments.Count > 0;
            var hasImages = state.ImageAttachments != null && state.ImageAttachments.Count > 0;

            if (!hasText && !hasContext && !hasImages)
            {
                ResumeStateStore.SaveInputFieldState(null);
                return;
            }

            ResumeStateStore.SaveInputFieldState(state);

            if (SidekickSettings.instance.VerboseLogging)
            {
                Debug.Log($"[Ryx Sidekick] Saved input field state: text={hasText}, context={hasContext}, images={hasImages}");
            }
        }

        /// <summary>
        /// Loads and clears input field state from SessionState.
        /// Returns null if no state was persisted.
        /// </summary>
        internal static InputFieldState LoadAndClearInputFieldState()
        {
            var state = ResumeStateStore.LoadAndClearInputFieldState();
            var hasText = !string.IsNullOrEmpty(state?.InputText);

            if (SidekickSettings.instance.VerboseLogging)
            {
                Debug.Log($"[Ryx Sidekick] Loaded input field state: text={hasText}, context={state?.ContextAttachments?.Count ?? 0}, images={state?.ImageAttachments?.Count ?? 0}");
            }

            return state;
        }

        #endregion
    }
}
