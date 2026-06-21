// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal interface IMachineIdProvider
    {
        /// <summary>A stable per-machine identifier (raw; the server stores only its hash).</summary>
        string GetMachineId();
    }
}
