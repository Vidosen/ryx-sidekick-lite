// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;
using UnityEngine;

#if HAS_UNITY_MCP
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
#endif

namespace Ryx.Sidekick.Editor.Infrastructure
{
    /// <summary>
    /// NOTE: Not wired into the default UX since B1 (Coplay off by default). Still constructed for the
    /// onboarding MCP step (disabled in B2). Kept for potential re-enable.
    /// See Documentation~/McpRework/01-coplay-default-off.md.
    /// </summary>
    internal sealed class McpForUnityGateway : IMcpForUnityGateway
    {
        public bool IsInstalled =>
#if HAS_UNITY_MCP
            true;
#else
            false;
#endif

        public bool IsBridgeRunning =>
#if HAS_UNITY_MCP
            MCPServiceLocator.Bridge.IsRunning;
#else
            false;
#endif

        public int CurrentPort =>
#if HAS_UNITY_MCP
            MCPServiceLocator.Bridge.CurrentPort;
#else
            0;
#endif

        public Task<bool> StartAsync()
        {
#if HAS_UNITY_MCP
            return MCPServiceLocator.Bridge.StartAsync();
#else
            return Task.FromResult(false);
#endif
        }

        public async Task<(bool success, string message)> VerifyAsync()
        {
#if HAS_UNITY_MCP
            var result = await MCPServiceLocator.Bridge.VerifyAsync();
            return (result.Success, result.Message);
#else
            return (false, null);
#endif
        }

        public Task StopAsync()
        {
#if HAS_UNITY_MCP
            return MCPServiceLocator.Bridge.StopAsync();
#else
            return Task.CompletedTask;
#endif
        }

        public bool CanStartLocalServer()
        {
#if HAS_UNITY_MCP
            return MCPServiceLocator.Server.CanStartLocalServer();
#else
            return false;
#endif
        }

        public bool StartLocalHttpServer()
        {
#if HAS_UNITY_MCP
            return MCPServiceLocator.Server.StartLocalHttpServer();
#else
            return false;
#endif
        }

        public string GetRpcUrl()
        {
#if HAS_UNITY_MCP
            try
            {
                return HttpEndpointUtility.GetMcpRpcUrl();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[McpForUnityGateway] Failed to get MCP RPC URL: {ex.Message}");
                return null;
            }
#else
            return null;
#endif
        }
    }
}
