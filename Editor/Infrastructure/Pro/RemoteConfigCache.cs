// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using Newtonsoft.Json;
using Ryx.Sidekick.Editor.Domain.Pro;

namespace Ryx.Sidekick.Editor.Infrastructure.Pro
{
    internal sealed class RemoteConfigCache
    {
        private readonly string _path;
        public RemoteConfigCache() : this(null) { }

        public RemoteConfigCache(string directory)
        {
            var dir = directory ?? Path.Combine("UserSettings", "Sidekick");
            _path = Path.Combine(dir, "remote-config.cache.json");
        }

        public void Write(RemoteConfig config)
        {
            if (config == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, JsonConvert.SerializeObject(config));
        }

        public RemoteConfig Read()
        {
            try
            {
                if (!File.Exists(_path)) return null;
                return JsonConvert.DeserializeObject<RemoteConfig>(File.ReadAllText(_path));
            }
            catch { return null; }
        }
    }
}
