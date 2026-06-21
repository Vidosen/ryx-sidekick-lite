// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;

namespace Ryx.Sidekick.Editor.Domain.Pro
{
    internal sealed class ProOfferManifest
    {
        public int SchemaVersion { get; set; }
        public bool Enabled { get; set; }
        public string Headline { get; set; }
        public string Subhead { get; set; }
        public string RequiresLiteVersion { get; set; }
        public List<ProFeatureDescriptor> Features { get; set; } = new List<ProFeatureDescriptor>();
        public ProCtaDescriptor Cta { get; set; }
    }
}
