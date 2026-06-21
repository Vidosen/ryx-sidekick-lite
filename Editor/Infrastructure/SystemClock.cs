// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    internal sealed class SystemClock : IClock
    {
        public DateTime Now => DateTime.Now;
    }
}
