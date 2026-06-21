// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Infrastructure.Auth;
using Ryx.Sidekick.Editor.Domain.Auth;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    internal sealed class DefaultCredentialStoreProvider : ICredentialStoreProvider
    {
        public ICredentialStore Create()
        {
            return CredentialStoreFactory.Create();
        }
    }
}
