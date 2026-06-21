// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal interface IMessageElementFactory
    {
        VisualElement CreateMessageElement(Message message);

        /// <summary>
        /// Creates a message element and applies role-header visibility if applicable.
        /// </summary>
        VisualElement CreateMessageElement(Message message, bool showRoleHeader);

        void UpdateToolElement(ToolCallElement element, ToolUse toolUse);
    }
}
