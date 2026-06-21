// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// Minimal logging contract used by Application use cases and Domain-adjacent
    /// services. Adapters in Infrastructure forward calls to the host (Unity
    /// <c>Debug</c>, file logs, etc.) — Application code never touches Unity APIs
    /// directly.
    /// </summary>
    internal interface ILogger
    {
        void Log(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogVerbose(string message);
    }
}
