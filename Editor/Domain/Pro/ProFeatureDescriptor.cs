// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Domain.Pro
{
    internal sealed class ProFeatureDescriptor
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Icon { get; set; }            // catalog key, resolved in Presentation
        public string ShortDescription { get; set; }
        public string Surface { get; set; }         // e.g. "provider:cursor"; null = modal-only
    }
}
