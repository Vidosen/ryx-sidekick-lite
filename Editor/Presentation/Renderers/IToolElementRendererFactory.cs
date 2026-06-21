// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal interface IToolElementRendererFactory
    {
        IReadOnlyDictionary<ToolKind, IToolElementRenderer> CreateRendererMap();
        IToolElementRenderer CreateFallbackRenderer();
    }
}
