// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;
using Newtonsoft.Json;
using Ryx.Sidekick.Editor.Domain.Pro;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Pro
{
    internal sealed class RemoteConfigSource : IRemoteConfigSource
    {
        private readonly IHttpClient _http;
        private readonly RemoteConfigCache _cache;
        private readonly string _url;
        private RemoteConfig _current;

        public RemoteConfigSource(IHttpClient http, RemoteConfigCache cache, IBakedConfigSource baked)
        {
            _http = http;
            _cache = cache;
            _url = ProOfferEndpoints.ManifestUrl;
            _current = _cache.Read() ?? baked?.Load();
        }

        public RemoteConfig Current => _current;

        public async Task RefreshAsync()
        {
            var result = await _http.GetAsync(_url, ProOfferEndpoints.TimeoutSeconds);
            if (!result.IsSuccess || string.IsNullOrEmpty(result.Body)) return;

            RemoteConfig parsed;
            try { parsed = JsonConvert.DeserializeObject<RemoteConfig>(result.Body); }
            catch { return; }

            if (!RemoteConfigValidator.IsValidEnvelope(parsed)) return;
            _cache.Write(parsed);
            _current = parsed;
        }
    }
}
