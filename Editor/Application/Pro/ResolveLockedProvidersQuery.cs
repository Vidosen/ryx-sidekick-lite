// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Pro;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Pro
{
    internal sealed class ResolveLockedProvidersQuery
    {
        private const string ProviderPrefix = "provider:";

        public IReadOnlyList<ProFeatureDescriptor> Resolve(ProOfferManifest offer, IProviderCatalog catalog)
        {
            if (offer?.Features == null || catalog == null) return Array.Empty<ProFeatureDescriptor>();

            var registered = new HashSet<string>(
                catalog.AllProviders?.Select(p => p.Id) ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);

            return offer.Features
                .Where(f => !string.IsNullOrEmpty(f.Surface) && f.Surface.StartsWith(ProviderPrefix, StringComparison.Ordinal))
                .Where(f => !registered.Contains(f.Surface.Substring(ProviderPrefix.Length)))
                .ToList();
        }
    }
}
