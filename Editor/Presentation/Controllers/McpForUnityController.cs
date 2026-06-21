// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Presentation.Views;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure.Mcp
{
    /// <summary>
    /// Controller for managing MCP for Unity server connection status and controls.
    /// Uses a gateway so the controller no longer knows about MCPServiceLocator directly.
    /// </summary>
    /// <remarks>
    /// NOTE: Not wired into the default UX since B1 (Coplay off by default). Still constructed for the
    /// onboarding MCP step (disabled in B2). Kept for potential re-enable.
    /// See Documentation~/McpRework/01-coplay-default-off.md.
    /// </remarks>
    internal sealed class McpForUnityController : IDisposable
    {
        public enum McpStatus
        {
            NotInstalled,
            Off,
            Starting,
            Connected,
            Stopping,
            Error
        }

        private const string McpForUnityGitUrl = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity";

        private readonly IMcpForUnityGateway _gateway;
        private readonly IEditorScheduler _scheduler;

        private IStatusBarView _statusBarView;
        private McpStatus _status;
        private bool _isOperationInProgress;
        private CancellationTokenSource _pollCancellation;
        private string _lastErrorMessage;
        private bool _isInstalling;
        private AddRequest _packageAddRequest;

        public bool IsPackageInstalled => _gateway?.IsInstalled == true;

        public McpStatus Status => _status;

        public event Action<McpStatus> OnStatusChanged;

        public McpForUnityController(IMcpForUnityGateway gateway = null, IEditorScheduler scheduler = null)
        {
            _gateway = gateway ?? new McpForUnityGateway();
            _scheduler = scheduler ?? new UnityEditorScheduler();
            _status = IsPackageInstalled ? McpStatus.Off : McpStatus.NotInstalled;
        }

        public void BindView(IStatusBarView statusBarView)
        {
            _statusBarView = statusBarView;
            UpdateSectionVisibility();
            UpdateUI();

            if (IsPackageInstalled)
            {
                StartPolling();
            }
        }

        public void Dispose()
        {
            StopPolling();
            EditorApplication.update -= OnPackageInstallProgress;
            _statusBarView = null;
        }

        /// <summary>
        /// Attempts to connect to an already running MCP server without starting it.
        /// </summary>
        public async void TryAutoConnect()
        {
            if (!IsPackageInstalled || _isOperationInProgress)
            {
                return;
            }

            try
            {
                if (_gateway.IsBridgeRunning)
                {
                    var result = await _gateway.VerifyAsync();
                    UpdateStatus(result.success ? McpStatus.Connected : McpStatus.Error,
                        result.success ? null : result.message);
                    return;
                }

                try
                {
                    var started = await _gateway.StartAsync();
                    if (started)
                    {
                        var result = await _gateway.VerifyAsync();
                        UpdateStatus(result.success ? McpStatus.Connected : McpStatus.Off,
                            result.success ? null : result.message);
                    }
                    else
                    {
                        UpdateStatus(McpStatus.Off);
                    }
                }
                catch
                {
                    UpdateStatus(McpStatus.Off);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Ryx Sidekick] MCP auto-connect error: {ex.Message}");
                UpdateStatus(McpStatus.Off);
            }
        }

        /// <summary>
        /// Starts the MCP server and connects to it.
        /// </summary>
        public async void StartServerAndConnect()
        {
            if (!IsPackageInstalled || _isOperationInProgress)
            {
                return;
            }

            try
            {
                _isOperationInProgress = true;
                UpdateStatus(McpStatus.Starting);

                if (!_gateway.CanStartLocalServer())
                {
                    var started = await _gateway.StartAsync();
                    if (started)
                    {
                        var result = await _gateway.VerifyAsync();
                        if (result.success)
                        {
                            UpdateStatus(McpStatus.Connected);
                            return;
                        }
                    }

                    UpdateStatus(McpStatus.Error, "Cannot start local server. Check MCP for Unity settings.");
                    return;
                }

                if (!_gateway.StartLocalHttpServer())
                {
                    UpdateStatus(McpStatus.Error, "Failed to start MCP server");
                    return;
                }

                var maxAttempts = 30;
                for (int i = 0; i < maxAttempts; i++)
                {
                    await Task.Delay(1000);

                    try
                    {
                        var started = await _gateway.StartAsync();
                        if (started)
                        {
                            var result = await _gateway.VerifyAsync();
                            if (result.success)
                            {
                                UpdateStatus(McpStatus.Connected);
                                return;
                            }
                        }
                    }
                    catch
                    {
                        // Server not ready yet, continue waiting.
                    }
                }

                UpdateStatus(McpStatus.Error, "Timeout waiting for MCP server");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Ryx Sidekick] MCP start error: {ex.Message}");
                UpdateStatus(McpStatus.Error, ex.Message);
            }
            finally
            {
                _isOperationInProgress = false;
            }
        }

        /// <summary>
        /// Stops the MCP bridge connection and optionally the server.
        /// </summary>
        public async void Stop()
        {
            if (!IsPackageInstalled || _isOperationInProgress)
            {
                return;
            }

            try
            {
                _isOperationInProgress = true;
                UpdateStatus(McpStatus.Stopping);

                await _gateway.StopAsync();

                UpdateStatus(McpStatus.Off);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Ryx Sidekick] MCP stop error: {ex.Message}");
                UpdateStatus(McpStatus.Error, ex.Message);
            }
            finally
            {
                _isOperationInProgress = false;
            }
        }

        /// <summary>
        /// Refreshes the current MCP status using VerifyAsync for accurate connection state.
        /// </summary>
        public async void RefreshStatus()
        {
            if (!IsPackageInstalled || _isOperationInProgress)
            {
                return;
            }

            try
            {
                if (!_gateway.IsBridgeRunning)
                {
                    UpdateStatus(McpStatus.Off);
                    return;
                }

                var result = await _gateway.VerifyAsync();
                if (result.success)
                {
                    UpdateStatus(McpStatus.Connected, $"Port {_gateway.CurrentPort}");
                }
                else
                {
                    UpdateStatus(McpStatus.Off);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Ryx Sidekick] MCP status refresh error: {ex.Message}");
                UpdateStatus(McpStatus.Error, ex.Message);
            }
        }

        /// <summary>
        /// Called when the window gains focus - refreshes status.
        /// </summary>
        public void OnWindowFocus()
        {
            if (IsPackageInstalled)
            {
                RefreshStatus();
            }
        }

        /// <summary>
        /// Gets the MCP RPC URL if available.
        /// </summary>
        public string GetMcpRpcUrl()
        {
            if (IsPackageInstalled && _status == McpStatus.Connected)
            {
                return _gateway.GetRpcUrl();
            }

            return null;
        }

        private void UpdateStatus(McpStatus status, string errorMessage = null)
        {
            _status = status;
            _lastErrorMessage = errorMessage;

            _scheduler.Schedule(UpdateUI);

            OnStatusChanged?.Invoke(status);
        }

        private void UpdateUI()
        {
            if (_statusBarView == null)
            {
                return;
            }

            var isTransitioning = _status == McpStatus.Starting || _status == McpStatus.Stopping;
            _statusBarView.SetMcpStatus(
                GetIndicatorState(),
                _status switch
                {
                    McpStatus.NotInstalled => "MCP: Not installed",
                    McpStatus.Off => "MCP: Off",
                    McpStatus.Starting => "MCP: Starting...",
                    McpStatus.Connected => "MCP: Connected",
                    McpStatus.Stopping => "MCP: Stopping...",
                    McpStatus.Error => "MCP: Error",
                    _ => "MCP"
                },
                _status switch
                {
                    McpStatus.Connected => "Stop",
                    McpStatus.Starting => "Starting...",
                    McpStatus.Stopping => "Stopping...",
                    McpStatus.Error => "Retry",
                    McpStatus.NotInstalled => _isInstalling ? "Installing..." : "Install",
                    _ => "Start"
                },
                buttonVisible: true,
                buttonEnabled: !isTransitioning && !_isInstalling,
                tooltip: _status == McpStatus.Error && !string.IsNullOrEmpty(_lastErrorMessage)
                    ? _lastErrorMessage
                    : null);
        }

        /// <summary>
        /// Installs the MCP for Unity package from git.
        /// </summary>
        public void InstallPackage()
        {
            if (_isInstalling)
            {
                return;
            }

            _isInstalling = true;
            UpdateUI();

            _packageAddRequest = Client.Add(McpForUnityGitUrl);
            EditorApplication.update += OnPackageInstallProgress;
        }

        private void OnPackageInstallProgress()
        {
            if (_packageAddRequest is not { IsCompleted: true })
            {
                return;
            }

            EditorApplication.update -= OnPackageInstallProgress;
            _isInstalling = false;

            if (_packageAddRequest.Status == StatusCode.Success)
            {
                Debug.Log("[Ryx Sidekick] MCP for Unity package installed successfully.");
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            else
            {
                Debug.LogError($"[Ryx Sidekick] Failed to install MCP for Unity: {_packageAddRequest.Error?.message}");
                UpdateUI();
            }

            _packageAddRequest = null;
        }

        private void UpdateSectionVisibility()
        {
            _statusBarView?.SetMcpSectionVisible(true);
        }

        private void StartPolling()
        {
            StopPolling();
            _pollCancellation = new CancellationTokenSource();
            _ = PollStatusLoopAsync(_pollCancellation.Token);
        }

        private void StopPolling()
        {
            _pollCancellation?.Cancel();
            _pollCancellation?.Dispose();
            _pollCancellation = null;
        }

        private async Task PollStatusLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    RefreshStatus();
                }
            }
        }

        private IndicatorState GetIndicatorState()
        {
            return _status switch
            {
                McpStatus.Connected => IndicatorState.Success,
                McpStatus.Starting or McpStatus.Stopping => IndicatorState.Checking,
                McpStatus.Error => IndicatorState.Error,
                _ => IndicatorState.Neutral
            };
        }
    }
}
