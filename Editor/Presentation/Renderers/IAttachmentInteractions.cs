// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal interface IAttachmentInteractions
    {
        void OpenContextAttachment(IContextAttachment attachment, Message message);
        void OpenImagePreview(Texture2D texture);
        Texture2D ResolveImageTexture(ImageAttachment attachment);
    }
}
