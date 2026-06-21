// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class ToolRendererRegistry : IToolRendererRegistry
    {
        private readonly IReadOnlyDictionary<ToolKind, IToolElementRenderer> _byKind;
        private readonly IToolElementRenderer _fallback;

        public ToolRendererRegistry(IToolElementRendererFactory factory)
        {
            _byKind = factory.CreateRendererMap();
            _fallback = factory.CreateFallbackRenderer();
        }

        public IToolElementRenderer Resolve(ToolKind kind) => _byKind.GetValueOrDefault(kind, _fallback);
    }
}
