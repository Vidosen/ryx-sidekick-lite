// SPDX-License-Identifier: GPL-3.0-only
using Newtonsoft.Json;
using Ryx.Sidekick.Editor.Domain.Pro;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure.Pro
{
    internal sealed class BakedConfigSource : IBakedConfigSource
    {
        public RemoteConfig Load()
        {
            var asset = Resources.Load<TextAsset>("SidekickBakedConfig");
            if (asset == null) return null;
            return JsonConvert.DeserializeObject<RemoteConfig>(asset.text);
        }
    }
}
