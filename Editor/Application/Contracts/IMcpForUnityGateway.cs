// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;

namespace Ryx.Sidekick.Editor
{
    internal interface IMcpForUnityGateway
    {
        bool IsInstalled { get; }

        bool IsBridgeRunning { get; }

        int CurrentPort { get; }

        Task<bool> StartAsync();

        Task<(bool success, string message)> VerifyAsync();

        Task StopAsync();

        bool CanStartLocalServer();

        bool StartLocalHttpServer();

        string GetRpcUrl();
    }
}
