// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Attachments
{
    internal sealed class RemoveAttachmentUseCase
    {
        private readonly AttachmentSessionState _state;

        public RemoveAttachmentUseCase(AttachmentSessionState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Removes an image by id. If the image has a <see cref="ImageAttachment.LinkedContextAttachmentId"/>,
        /// also removes that context attachment.
        /// </summary>
        public RemoveAttachmentResult RemoveImage(string imageId)
        {
            if (!_state.RemoveImageById(imageId, out var removedImage))
                return new RemoveAttachmentResult(removed: false, removedImage: null, removedContext: null);

            IContextAttachment removedContext = null;
            if (!string.IsNullOrEmpty(removedImage.LinkedContextAttachmentId))
            {
                _state.RemoveContextById(removedImage.LinkedContextAttachmentId, out removedContext);
            }

            return new RemoveAttachmentResult(removed: true, removedImage: removedImage, removedContext: removedContext);
        }

        /// <summary>
        /// Removes a context attachment by id. If it is a <see cref="ScreenshotContextAttachment"/>
        /// with a <see cref="ScreenshotContextAttachment.LinkedImageAttachmentId"/>, also removes that image.
        /// </summary>
        public RemoveAttachmentResult RemoveContext(string contextId)
        {
            if (!_state.RemoveContextById(contextId, out var removedContext))
                return new RemoveAttachmentResult(removed: false, removedImage: null, removedContext: null);

            ImageAttachment removedImage = null;
            if (removedContext is ScreenshotContextAttachment screenshot &&
                !string.IsNullOrEmpty(screenshot.LinkedImageAttachmentId))
            {
                _state.RemoveImageById(screenshot.LinkedImageAttachmentId, out removedImage);
            }

            return new RemoveAttachmentResult(removed: true, removedImage: removedImage, removedContext: removedContext);
        }
    }

    internal readonly struct RemoveAttachmentResult
    {
        public bool Removed { get; }
        public ImageAttachment RemovedImage { get; }
        public IContextAttachment RemovedContext { get; }

        public RemoveAttachmentResult(bool removed, ImageAttachment removedImage, IContextAttachment removedContext)
        {
            Removed = removed;
            RemovedImage = removedImage;
            RemovedContext = removedContext;
        }
    }
}
