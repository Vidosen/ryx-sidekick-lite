// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Licensing
{
    internal sealed class SystemInfoMachineIdProvider : IMachineIdProvider
    {
        public string GetMachineId() => SystemInfo.deviceUniqueIdentifier;
    }
}
