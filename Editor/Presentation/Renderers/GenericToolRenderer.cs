// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class GenericToolRenderer : IToolElementRenderer
    {
        public bool CanRender(ToolUse toolUse) => true; // catch-all fallback
        public VisualElement BuildHeaderContent(ToolUse toolUse) => null;
        public VisualElement BuildBodyContent(ToolUse toolUse) => null;
    }
}
