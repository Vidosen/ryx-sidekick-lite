// SPDX-License-Identifier: GPL-3.0-only
using System;
using Unity.AppUI.Redux;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    [Serializable]
    internal sealed class SidekickAppState : PartitionedState, IPartionableState<SidekickAppState>
    {
        public SidekickAppState()
        {
        }

        private SidekickAppState(PartitionedState state)
            : base(state)
        {
        }

        public new SidekickAppState Set<TSliceState>(string sliceName, TSliceState sliceState)
        {
            if (string.IsNullOrEmpty(sliceName))
            {
                throw new ArgumentException("Slice name cannot be null or empty.", nameof(sliceName));
            }

            var copy = new SidekickAppState(this)
            {
                [sliceName] = sliceState
            };

            return copy;
        }
    }
}
