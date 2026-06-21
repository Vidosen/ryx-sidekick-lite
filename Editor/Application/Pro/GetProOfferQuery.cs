// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Pro;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Pro
{
    internal sealed class GetProOfferQuery
    {
        private readonly IRemoteConfigSource _source;
        public GetProOfferQuery(IRemoteConfigSource source) => _source = source;

        public ProOfferManifest Get()
        {
            var offer = _source.Current?.Offer;
            return RemoteConfigValidator.IsValidOffer(offer) ? offer : null;
        }

        public Task RefreshAsync() => _source.RefreshAsync();
    }
}
