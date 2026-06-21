// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor
{
    internal interface ICredentialStoreProvider
    {
        ICredentialStore Create();
    }
}
