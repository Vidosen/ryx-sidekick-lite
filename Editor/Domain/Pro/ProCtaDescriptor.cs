// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Domain.Pro
{
    internal sealed class ProCtaDescriptor
    {
        public string Label { get; set; }
        public string Url { get; set; }

        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public ProCtaMode Mode { get; set; }

        public string Price { get; set; } // nullable; hide when null/empty
    }
}
