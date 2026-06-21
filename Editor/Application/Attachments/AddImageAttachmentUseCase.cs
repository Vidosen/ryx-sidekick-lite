// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Attachments
{
    internal sealed class AddImageAttachmentUseCase
    {
        private readonly AttachmentSessionState _state;

        public AddImageAttachmentUseCase(AttachmentSessionState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public AddImageAttachmentResult Execute(ImageAttachment image)
        {
            if (image == null)
                return new AddImageAttachmentResult(added: false, attachment: null);

            if (string.IsNullOrEmpty(image.Id))
                image.Id = Guid.NewGuid().ToString("N");

            _state.AppendImage(image);
            return new AddImageAttachmentResult(added: true, attachment: image);
        }
    }

    internal readonly struct AddImageAttachmentResult
    {
        public bool Added { get; }
        public ImageAttachment Attachment { get; }

        public AddImageAttachmentResult(bool added, ImageAttachment attachment)
        {
            Added = added;
            Attachment = attachment;
        }
    }
}
