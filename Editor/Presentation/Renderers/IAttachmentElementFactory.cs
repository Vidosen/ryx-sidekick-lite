// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal interface IAttachmentElementFactory
    {
        VisualElement CreateContextAttachmentsContainer(
            IReadOnlyList<IContextAttachment> attachments,
            Message message,
            IAttachmentInteractions interactions);

        VisualElement CreateImageAttachmentsContainer(
            IReadOnlyList<ImageAttachment> attachments,
            IAttachmentInteractions interactions);
    }
}
