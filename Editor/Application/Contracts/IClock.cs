// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor
{
    internal interface IClock
    {
        DateTime Now { get; }
    }
}
