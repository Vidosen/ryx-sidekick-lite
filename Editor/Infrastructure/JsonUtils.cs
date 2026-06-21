// SPDX-License-Identifier: GPL-3.0-only
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ryx.Sidekick.Editor
{
    internal static class JsonUtils
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Include,
            DateParseHandling = DateParseHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore
        };

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            try
            {
                return JsonConvert.DeserializeObject<T>(json, Settings);
            }
            catch
            {
                return default;
            }
        }

        public static JToken DeserializeToToken(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JToken.Parse(json);
        }

        public static string Serialize(object value)
        {
            if (value == null) return string.Empty;
            return JsonConvert.SerializeObject(value, Settings);
        }
    }
}

