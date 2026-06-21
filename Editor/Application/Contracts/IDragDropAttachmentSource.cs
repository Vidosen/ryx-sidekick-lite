// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using UnityEngine;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// A dragged Unity Object reference together with its resolved asset path.
    /// The asset-path lookup is performed by the infrastructure adapter so that
    /// Application-layer code stays free of editor API dependencies.
    /// </summary>
    internal readonly struct DraggedContextObject
    {
        /// <summary>The UnityEngine.Object that was dragged (may be null for path-only items).</summary>
        public Object Reference { get; }

        /// <summary>
        /// The asset-relative path returned by AssetDatabase.GetAssetPath, or an empty
        /// string when the object has no persistent asset path (e.g. a scene-only GameObject).
        /// </summary>
        public string AssetPath { get; }

        public DraggedContextObject(Object reference, string assetPath)
        {
            Reference = reference;
            AssetPath = assetPath ?? string.Empty;
        }
    }

    /// <summary>
    /// Abstraction over the current drag-and-drop payload so that Application-layer
    /// attachment workflows do not call the DragAndDrop editor API directly.
    ///
    /// Drag initiation (UI Toolkit DragUpdatedEvent / DragPerformEvent) remains in the
    /// Presentation layer. This contract covers only payload inspection — turning the
    /// active drag into paths / Object references.
    /// </summary>
    internal interface IDragDropAttachmentSource
    {
        /// <summary>Returns true when the current drag payload contains at least one image file path or Texture2D object.</summary>
        bool HasImageDrag();

        /// <summary>
        /// Returns true when the current drag payload contains at least one context-compatible
        /// object (GameObject, MonoScript, TextAsset, or a text-file path).
        /// </summary>
        bool HasContextDrag();

        /// <summary>
        /// Returns the UnityEngine.Object references in the current drag payload, each paired
        /// with its resolved asset path. The path lookup is performed inside the infrastructure
        /// adapter so callers do not need to reference editor APIs.
        /// </summary>
        IReadOnlyList<DraggedContextObject> GetContextObjects();

        /// <summary>Returns the raw file paths in the current drag payload.</summary>
        IReadOnlyList<string> GetContextPaths();
    }
}
