// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor
{
    internal interface IEditorScheduler
    {
        void Schedule(Action action);
    }
}
