// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Pro
{
    internal sealed class SidekickProPresence : IProPresence
    {
#if SIDEKICK_PRO
        public bool IsInstalled => true;
#else
        public bool IsInstalled => false;
#endif
    }
}
