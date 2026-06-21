// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Ryx.Sidekick.Editor.UseCases.Attachments;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Views;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    /// <summary>
    /// Owns the open/close orchestration of the image overlay view. Pan/zoom remains
    /// inside ImageOverlayView; this presenter only decides what texture to show.
    /// </summary>
    internal sealed class ImageOverlayPresenter : IDisposable
    {
        private readonly IImageOverlayView _view;
        private readonly AttachmentSessionState _state;
        private readonly IImageTextureResolver _resolver;
        private bool _disposed;

        public ImageOverlayPresenter(
            IImageOverlayView view,
            AttachmentSessionState state,
            IImageTextureResolver resolver)
        {
            _view = view;
            _state = state;
            _resolver = resolver;
            if (_view != null) _view.ClosedRequested += Close;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_view != null) _view.ClosedRequested -= Close;
        }

        /// <summary>
        /// Pending image chip click: resolve the image attachment by id from state, resolve
        /// texture from resolver, and show.
        /// </summary>
        public void OpenForPendingImage(string imageId)
        {
            if (string.IsNullOrEmpty(imageId)) return;

            var attachment = _state.FindImageById(imageId);
            var texture = _resolver.Resolve(attachment);
            if (texture != null)
            {
                _view?.Show(texture);
            }
        }

        /// <summary>
        /// Pending screenshot context chip click: resolves linked image via state and shows.
        /// </summary>
        public void OpenForPendingScreenshot(ScreenshotContextAttachment screenshot)
        {
            if (screenshot == null || string.IsNullOrEmpty(screenshot.LinkedImageAttachmentId)) return;

            var imageAttachment = _state.FindImageById(screenshot.LinkedImageAttachmentId);
            if (imageAttachment == null) return;

            var texture = _resolver.Resolve(imageAttachment);
            if (texture != null)
            {
                _view?.Show(texture);
            }
        }

        /// <summary>
        /// History/message screenshot chip click: resolves linked image via message.Attachments
        /// using three fallback rules:
        /// 1. screenshot.LinkedImageAttachmentId → direct match in message.Attachments
        /// 2. Reverse-link via attachment.LinkedContextAttachmentId == screenshot.Id
        /// 3. Single-attachment fallback when message has exactly one attachment
        /// </summary>
        public void OpenForMessageScreenshot(ScreenshotContextAttachment screenshot, Message message)
        {
            if (screenshot == null || message?.Attachments == null) return;

            ImageAttachment linkedImage = null;

            if (!string.IsNullOrEmpty(screenshot.LinkedImageAttachmentId))
            {
                linkedImage = message.Attachments.FirstOrDefault(a => a?.Id == screenshot.LinkedImageAttachmentId);
            }

            if (linkedImage == null)
            {
                linkedImage = message.Attachments.FirstOrDefault(a => a?.LinkedContextAttachmentId == screenshot.Id);
            }

            if (linkedImage == null && message.Attachments.Count == 1)
            {
                linkedImage = message.Attachments[0];
            }

            if (linkedImage == null) return;

            var tex = _resolver.Resolve(linkedImage);
            if (tex != null)
            {
                _view?.Show(tex);
            }
        }

        /// <summary>
        /// Direct texture preview (no attachment context).
        /// </summary>
        public void OpenForTexture(Texture2D texture)
        {
            if (texture == null) return;
            _view?.Show(texture);
        }

        /// <summary>
        /// Hides the overlay.
        /// </summary>
        public void Close()
        {
            _view?.Hide();
        }
    }
}
