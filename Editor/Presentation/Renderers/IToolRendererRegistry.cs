// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal interface IToolRendererRegistry
    {
        IToolElementRenderer Resolve(ToolKind kind);
    }
}
