// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal interface IToolElementRenderer
    {
        bool CanRender(ToolUse toolUse);
        VisualElement BuildHeaderContent(ToolUse toolUse); // null = no inline header
        VisualElement BuildBodyContent(ToolUse toolUse);   // null = use default foldouts
    }
}
