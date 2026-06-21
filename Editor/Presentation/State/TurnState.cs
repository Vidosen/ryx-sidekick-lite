// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Presentation.State
{
    [Serializable]
    internal sealed class TurnState : IEquatable<TurnState>
    {
        internal static readonly TurnState Default = new(false, false, 0, 0);

        internal TurnState(bool isTurnActive, bool isStopping, int lastUsedTokens, int lastContextWindow)
        {
            IsTurnActive = isTurnActive;
            IsStopping = isStopping;
            LastUsedTokens = lastUsedTokens;
            LastContextWindow = lastContextWindow;
        }

        public bool IsTurnActive { get; }

        public bool IsStopping { get; }

        public int LastUsedTokens { get; }

        public int LastContextWindow { get; }

        internal TurnState With(
            bool? isTurnActive = null,
            bool? isStopping = null,
            int? lastUsedTokens = null,
            int? lastContextWindow = null)
        {
            return new TurnState(
                isTurnActive ?? IsTurnActive,
                isStopping ?? IsStopping,
                lastUsedTokens ?? LastUsedTokens,
                lastContextWindow ?? LastContextWindow);
        }

        public bool Equals(TurnState other)
        {
            return other != null
                && IsTurnActive == other.IsTurnActive
                && IsStopping == other.IsStopping
                && LastUsedTokens == other.LastUsedTokens
                && LastContextWindow == other.LastContextWindow;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TurnState);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = IsTurnActive.GetHashCode();
                hashCode = (hashCode * 397) ^ IsStopping.GetHashCode();
                hashCode = (hashCode * 397) ^ LastUsedTokens;
                hashCode = (hashCode * 397) ^ LastContextWindow;
                return hashCode;
            }
        }
    }
}
