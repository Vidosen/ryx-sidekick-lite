// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    internal static class ImageOverlayGeometry
    {
        public static Rect CalculateContainedImageRect(Vector2 viewportSize, Vector2 textureSize)
        {
            if (viewportSize.x <= 0f || viewportSize.y <= 0f || textureSize.x <= 0f || textureSize.y <= 0f)
            {
                return Rect.zero;
            }

            float scale = Mathf.Min(viewportSize.x / textureSize.x, viewportSize.y / textureSize.y);
            Vector2 containedSize = textureSize * scale;
            Vector2 offset = (viewportSize - containedSize) * 0.5f;
            return new Rect(offset, containedSize);
        }

        public static Vector2 ClampPan(
            Vector2 pan,
            float zoom,
            Vector2 viewportSize,
            Vector2 textureSize)
        {
            Rect containedRect = CalculateContainedImageRect(viewportSize, textureSize);
            if (containedRect.width <= 0f || containedRect.height <= 0f || zoom <= 0f)
            {
                return Vector2.zero;
            }

            Vector2 scaledOffset = containedRect.position * zoom;
            Vector2 scaledSize = containedRect.size * zoom;

            pan.x = ClampAxis(pan.x, scaledOffset.x, scaledSize.x, viewportSize.x);
            pan.y = ClampAxis(pan.y, scaledOffset.y, scaledSize.y, viewportSize.y);
            return pan;
        }

        public static Vector2 CalculateNextClampedPan(
            Vector2 startPointer,
            Vector2 startOffset,
            Vector2 currentPointer,
            float zoom,
            Vector2 viewportSize,
            Vector2 textureSize,
            out Vector2 nextStartPointer,
            out Vector2 nextStartOffset)
        {
            Vector2 delta = currentPointer - startPointer;
            Vector2 pan = ClampPan(startOffset + delta, zoom, viewportSize, textureSize);
            nextStartPointer = currentPointer;
            nextStartOffset = pan;
            return pan;
        }

        private static float ClampAxis(float pan, float scaledOffset, float scaledSize, float viewportSize)
        {
            if (scaledSize <= viewportSize)
            {
                return 0f;
            }

            float minPan = viewportSize - scaledSize - scaledOffset;
            float maxPan = -scaledOffset;
            return Mathf.Clamp(pan, minPan, maxPan);
        }
    }
}
