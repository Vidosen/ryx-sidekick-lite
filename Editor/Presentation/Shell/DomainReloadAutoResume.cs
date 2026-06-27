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

            // Clean-quit SHUTDOWN: a real Editor quit is NOT a domain reload (we deliberately do NOT set
            // AgentHostReloadCoordinator.IsReloadTeardownInProgress here). The daemon child should stop
            // immediately rather than linger out its ~30s grace, so tell each live daemon to SHUTDOWN
            // (stop its children + exit). Strictly gated + no-op when the feature is off or no daemon was
            // ever resolved this session — see AgentHostEndpointRegistry.
            ShutdownAgentHostDaemonsOnQuit();
        }

        /// <summary>
        /// On a clean Editor quit, send SHUTDOWN to every Agent Host daemon endpoint this session
        /// resolved. Best-effort and bounded: a missing/dead daemon is silently skipped. Distinct from
        /// the domain-reload detach path — that one keeps the daemon alive; this one stops it.
        /// </summary>
        private static void ShutdownAgentHostDaemonsOnQuit()
        {
            // No daemon endpoints ⇒ flag is effectively off / never connected ⇒ pure no-op.
            var endpoints = AgentHostEndpointRegistry.Snapshot();
            if (endpoints.Count == 0)
                return;

            if (!SidekickSettings.instance.UseAgentHost)
                return;

            foreach (var endpoint in endpoints)
            {
                var sent = AgentHostShutdownClient.TrySendShutdown(endpoint, msg => Debug.LogWarning(msg));
                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[AgentHost] Clean-quit SHUTDOWN to {endpoint.Host}:{endpoint.Port}: {(sent ? "sent" : "unreachable")}.");
                }
            }

            AgentHostEndpointRegistry.Clear();
        }

        private static void OnBeforeAssemblyReload()
        {
            if (_isQuitting) return;

            // Tell the dispose chain that this teardown is a domain reload, NOT a user stop/close: a
            // RemoteProcessHost must DETACH (close its socket, keep the daemon child alive) instead of
            // STOP (kill). beforeAssemblyReload fires before SidekickWindow.OnDisable → AppHost.Dispose
            // → ProcessManager.Dispose → host.Stop(), so the flag is observable for the whole chain.
            // Statics reset across the reload, and OnAfterAssemblyReload clears it explicitly too.
            AgentHostReloadCoordinator.IsReloadTeardownInProgress = true;

            foreach (var host in SidekickWindowHostRegistry.Snapshot())
            {
                SaveInputFieldState(host);

                if (!host.IsTurnActive) continue;

                var sessionId = host.CurrentSessionId;
                if (string.IsNullOrEmpty(sessionId)) continue;
                var providerId = host.CurrentProviderId;
                var hostToken = host.HostToken;

                ResumeStateStore.SavePendingResume(hostToken, providerId, sessionId);

                // If the runtime is daemon-backed, snapshot the reconnect keys (session handle + durable
                // replay cursor) so the next domain re-attaches instead of resuming. No-op / no keys when
                // in-process (flag OFF), in which case the next domain uses the lossy -r resume.
                if (host.TryGetAgentHostReconnectSnapshot(out var sessionHandle, out var lastDurableSeq))
                {
                    ResumeStateStore.SaveAgentHostReconnect(hostToken, sessionHandle, lastDurableSeq);

                    if (SidekickSettings.instance.VerboseLogging)
                    {
                        Debug.Log($"[Ryx Sidekick] Agent Host reconnect snapshot saved: host={hostToken} handle={sessionHandle} durableSeq={lastDurableSeq}");
                    }
                }
                else
                {
                    // Clear any stale snapshot from a previous reload so we never replay-attach a session
                    // that is no longer daemon-backed.
                    ResumeStateStore.ClearAgentHostReconnect(hostToken);
                }

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
            // New domain: the reload-teardown window is over. (The static is already false in the fresh
            // domain; clearing it explicitly keeps same-domain edge cases and tests deterministic.)
            AgentHostReloadCoordinator.IsReloadTeardownInProgress = false;

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

            // Use delayCall to ensure UI is fully initialized, then decide attach-vs-resume:
            // a daemon-backed host re-attaches to the surviving turn (no synthetic prompt); otherwise
            // it falls back to the existing "Continue where you left off" resume (zero regression).
            Scheduler.Schedule(() =>
            {
                var outcome = DomainReloadReconnectCoordinator.Resume(
                    ResumeStateStore, targetHost, hostToken, providerId, sessionId);

                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[Ryx Sidekick] Post-reload resume outcome for host={hostToken}: {outcome}");
                }
            });
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
