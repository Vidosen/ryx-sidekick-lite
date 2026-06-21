// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Pro;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Pro
{
    internal sealed class GetMcpRecommendationsQuery
    {
        private readonly IRemoteConfigSource _source;
        public GetMcpRecommendationsQuery(IRemoteConfigSource source) => _source = source;

        public McpRecommendationsManifest Get()
        {
            var reco = _source.Current?.McpRecommendations;
            return RemoteConfigValidator.IsValidRecommendations(reco) ? reco : null;
        }

        public Task RefreshAsync() => _source.RefreshAsync();
    }
}
