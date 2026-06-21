// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Pro;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal interface IRemoteConfigSource
    {
        RemoteConfig Current { get; }
        Task RefreshAsync();
    }
}
