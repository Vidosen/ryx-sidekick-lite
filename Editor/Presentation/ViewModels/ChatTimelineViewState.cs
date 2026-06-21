// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    internal readonly struct ScrollDeltaSnapshot
    {
        public ScrollDeltaSnapshot(float value, float max, bool isAtBottom, bool isAtWindowBottom, bool isNearTop)
        {
            Value = value;
            Max = max;
            IsAtBottom = isAtBottom;
            IsAtWindowBottom = isAtWindowBottom;
            IsNearTop = isNearTop;
        }

        public float Value { get; }
        public float Max { get; }
        public bool IsAtBottom { get; }
        public bool IsAtWindowBottom { get; }
        public bool IsNearTop { get; }
    }
}
