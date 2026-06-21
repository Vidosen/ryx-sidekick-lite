// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Attachments
{
    internal sealed class AttachmentSessionState
    {
        private readonly List<ImageAttachment> _pendingImages = new();
        private readonly List<IContextAttachment> _pendingContexts = new();

        public IReadOnlyList<ImageAttachment> PendingImages => _pendingImages;
        public IReadOnlyList<IContextAttachment> PendingContexts => _pendingContexts;

        public event Action Changed;

        public bool HasPending => _pendingImages.Count > 0 || _pendingContexts.Count > 0;

        internal void AppendImage(ImageAttachment image)
        {
            if (image == null) return;
            _pendingImages.Add(image);
            Changed?.Invoke();
        }

        internal void AppendContext(IContextAttachment context)
        {
            if (context == null) return;
            _pendingContexts.Add(context);
            Changed?.Invoke();
        }

        internal bool RemoveImageById(string id, out ImageAttachment removed)
        {
            removed = null;
            if (string.IsNullOrEmpty(id)) return false;

            var index = _pendingImages.FindIndex(a => a?.Id == id);
            if (index < 0) return false;

            removed = _pendingImages[index];
            _pendingImages.RemoveAt(index);
            Changed?.Invoke();
            return true;
        }

        internal bool RemoveContextById(string id, out IContextAttachment removed)
        {
            removed = null;
            if (string.IsNullOrEmpty(id)) return false;

            var index = _pendingContexts.FindIndex(a => a?.Id == id);
            if (index < 0) return false;

            removed = _pendingContexts[index];
            _pendingContexts.RemoveAt(index);
            Changed?.Invoke();
            return true;
        }

        internal void Clear()
        {
            var hadContent = _pendingImages.Count > 0 || _pendingContexts.Count > 0;
            _pendingImages.Clear();
            _pendingContexts.Clear();
            if (hadContent)
            {
                Changed?.Invoke();
            }
        }

        internal void Restore(IEnumerable<ImageAttachment> images, IEnumerable<IContextAttachment> contexts)
        {
            _pendingImages.Clear();
            _pendingContexts.Clear();

            if (images != null)
            {
                foreach (var img in images)
                {
                    if (img != null) _pendingImages.Add(img);
                }
            }

            if (contexts != null)
            {
                foreach (var ctx in contexts)
                {
                    if (ctx != null) _pendingContexts.Add(ctx);
                }
            }

            Changed?.Invoke();
        }

        internal IContextAttachment FindContextById(string id)
        {
            return string.IsNullOrEmpty(id) ? null : _pendingContexts.FirstOrDefault(a => a?.Id == id);
        }

        internal ImageAttachment FindImageById(string id)
        {
            return string.IsNullOrEmpty(id) ? null : _pendingImages.FirstOrDefault(a => a?.Id == id);
        }
    }
}
