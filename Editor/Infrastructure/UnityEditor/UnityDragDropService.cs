// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    /// <summary>
    /// Infrastructure adapter that wraps UnityEditor.DragAndDrop for
    /// Application-layer attachment workflows.
    /// </summary>
    internal sealed class UnityDragDropService : IDragDropAttachmentSource
    {
        public bool HasImageDrag()
        {
            if (DragAndDrop.paths != null)
            {
                foreach (var p in DragAndDrop.paths)
                {
                    var mediaType = AttachmentUtils.GetMediaTypeFromPath(p);
                    if (!string.IsNullOrEmpty(mediaType) && mediaType.StartsWith("image/", System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (DragAndDrop.objectReferences != null)
            {
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is Texture2D)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool HasContextDrag()
        {
            if (DragAndDrop.objectReferences != null)
            {
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is GameObject) return true;
                    if (obj is MonoScript || obj is TextAsset) return true;

                    var path = AssetDatabase.GetAssetPath(obj);
                    if (ContextAttachmentService.IsTextFile(path)) return true;
                }
            }

            if (DragAndDrop.paths != null)
            {
                foreach (var path in DragAndDrop.paths)
                {
                    if (ContextAttachmentService.IsTextFile(path)) return true;
                }
            }

            return false;
        }

        public IReadOnlyList<DraggedContextObject> GetContextObjects()
        {
            if (DragAndDrop.objectReferences == null)
                return System.Array.Empty<DraggedContextObject>();

            var results = new List<DraggedContextObject>(DragAndDrop.objectReferences.Length);
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj == null) continue;
                var assetPath = AssetDatabase.GetAssetPath(obj);
                results.Add(new DraggedContextObject(obj, assetPath));
            }
            return results;
        }

        public IReadOnlyList<string> GetContextPaths()
        {
            if (DragAndDrop.paths == null)
                return System.Array.Empty<string>();
            return new List<string>(DragAndDrop.paths);
        }
    }
}
