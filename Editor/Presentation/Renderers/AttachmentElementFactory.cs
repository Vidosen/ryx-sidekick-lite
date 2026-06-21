// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal sealed class AttachmentElementFactory : IAttachmentElementFactory
    {
        public VisualElement CreateContextAttachmentsContainer(
            IReadOnlyList<IContextAttachment> attachments,
            Message message,
            IAttachmentInteractions interactions)
        {
            if (attachments == null || attachments.Count == 0) return null;

            var container = new VisualElement
            {
                name = "sk-context-attachments"
            };
            container.AddToClassList("sk-message-context");

            var title = new Label("Context");
            title.AddToClassList("sk-message-context__title");
            container.Add(title);

            var chips = new VisualElement();
            chips.AddToClassList("sk-message-context__chips");

            foreach (var attachment in attachments)
            {
                if (attachment == null) continue;

                var chip = new ContextChipElement();
                chip.SetAttachment(attachment, showRemoveButton: false);
                chip.OnClick += a => interactions.OpenContextAttachment(a, message);
                chips.Add(chip);
            }

            container.Add(chips);
            return container;
        }

        public VisualElement CreateImageAttachmentsContainer(
            IReadOnlyList<ImageAttachment> attachments,
            IAttachmentInteractions interactions)
        {
            if (attachments == null || attachments.Count == 0) return null;

            var attachmentsContainer = new VisualElement
            {
                name = "sk-attachments"
            };
            attachmentsContainer.AddToClassList("sk-attachments");

            foreach (var attachment in attachments)
            {
                attachmentsContainer.Add(CreateAttachmentThumbnail(attachment, interactions));
            }

            return attachmentsContainer;
        }

        private static VisualElement CreateAttachmentThumbnail(
            ImageAttachment attachment,
            IAttachmentInteractions interactions)
        {
            var wrapper = new VisualElement();
            wrapper.AddToClassList("sk-attachment-thumb");

            var tex = interactions.ResolveImageTexture(attachment);
            ApplyContainSize(wrapper, tex, 140f, 140f);

            var image = new Image();
            image.AddToClassList("sk-attachment-thumb__image");
            image.scaleMode = ScaleMode.ScaleToFit;
            image.image = tex;
            wrapper.Add(image);

            wrapper.RegisterCallback<ClickEvent>(_ =>
            {
                var fullTex = image.image as Texture2D ?? interactions.ResolveImageTexture(attachment);
                if (fullTex)
                {
                    interactions.OpenImagePreview(fullTex);
                }
            });

            return wrapper;
        }

        /// <summary>
        /// Applies "contain" sizing to a wrapper element based on the texture's aspect ratio.
        /// The element will fit within maxW x maxH while preserving aspect ratio.
        /// </summary>
        private static void ApplyContainSize(VisualElement wrapper, Texture2D tex, float maxW, float maxH)
        {
            if (wrapper == null) return;

            if (tex == null || tex.width <= 0 || tex.height <= 0)
            {
                // Fallback to square if no texture
                wrapper.style.width = maxW;
                wrapper.style.height = maxH;
                return;
            }

            float texW = tex.width;
            float texH = tex.height;
            float scale = Mathf.Min(maxW / texW, maxH / texH);

            wrapper.style.width = texW * scale;
            wrapper.style.height = texH * scale;
        }
    }
}
