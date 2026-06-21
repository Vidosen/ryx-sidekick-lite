// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Providers
{
    /// <summary>
    /// Describes a permission mode option for a CLI provider.
    /// </summary>
    internal readonly struct PermissionModeDescriptor
    {
        public string Value { get; }
        public string Label { get; }
        public string Icon { get; }

        public PermissionModeDescriptor(string value, string label, string icon = null)
        {
            Value = value;
            Label = label;
            Icon = icon;
        }

        public override string ToString() => Label;
    }
}
