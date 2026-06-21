// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Pro;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal interface IBakedConfigSource
    {
        RemoteConfig Load();
    }
}
