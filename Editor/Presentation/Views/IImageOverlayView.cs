// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal interface IImageOverlayView
    {
        event Action ClosedRequested;

        void Show(Texture2D texture);

        void Hide();
    }

    internal sealed class ImageOverlayView : IImageOverlayView
    {
        private const float MinZoom = 1f;
        private const float MaxZoom = 6f;
        private const float ZoomStep = 0.15f;

        private readonly VisualElement _overlay;
        private readonly VisualElement _content;
        private readonly VisualElement _viewport;
        private readonly Image _image;

        private float _zoom = 1f;
        private Vector2 _pan;
        private bool _isPanning;
        private Vector2 _panStartPointer;
        private Vector2 _panStartOffset;

        public ImageOverlayView(
            VisualElement overlay,
            VisualElement backdrop,
            VisualElement content,
            Button closeButton,
            VisualElement viewport,
            Image image)
        {
            _overlay = overlay;
            _content = content;
            _viewport = viewport;
            _image = image;

            closeButton?.RegisterCallback<ClickEvent>(_ => ClosedRequested?.Invoke());
            backdrop?.RegisterCallback<ClickEvent>(_ => ClosedRequested?.Invoke());
            _content?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            _viewport?.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            _overlay?.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                {
                    return;
                }

                ClosedRequested?.Invoke();
                evt.StopPropagation();
            });

            SetupZoomPan();
        }

        public event Action ClosedRequested;

        public void Show(Texture2D texture)
        {
            if (_image != null)
            {
                _image.image = texture;
            }

            if (_overlay != null)
            {
                _overlay.style.display = texture != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (texture == null)
            {
                return;
            }

            ResetTransform();
            _overlay?.schedule.Execute(() =>
            {
                if (_overlay?.style.display != DisplayStyle.Flex)
                {
                    return;
                }

                SyncImageToViewport();
                ReclampPan();
            });
        }

        public void Hide()
        {
            if (_image != null)
            {
                _image.image = null;
            }

            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
            }

            ResetTransform();
        }

        private void SetupZoomPan()
        {
            if (_viewport == null || _image == null)
            {
                return;
            }

            _image.style.transformOrigin = new TransformOrigin(0, 0, 0);

            _viewport.RegisterCallback<WheelEvent>(evt =>
            {
                evt.StopPropagation();
                if (!TryGetGeometry(out var viewportSize, out var textureSize))
                {
                    return;
                }

                float oldZoom = _zoom;
                float newZoom = Mathf.Clamp(oldZoom + (-evt.delta.y * ZoomStep), MinZoom, MaxZoom);
                if (Mathf.Approximately(oldZoom, newZoom))
                {
                    return;
                }

                Vector2 cursorPosition = _viewport.WorldToLocal(evt.mousePosition);
                Vector2 localPoint = (cursorPosition - _pan) / oldZoom;
                _pan = cursorPosition - localPoint * newZoom;
                _zoom = newZoom;
                _pan = ImageOverlayGeometry.ClampPan(_pan, _zoom, viewportSize, textureSize);
                ApplyTransform();
            });

            _viewport.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                _isPanning = true;
                _panStartPointer = evt.localPosition;
                _panStartOffset = _pan;
                _viewport.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            _viewport.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_isPanning || !TryGetGeometry(out var viewportSize, out var textureSize))
                {
                    return;
                }

                _pan = ImageOverlayGeometry.CalculateNextClampedPan(
                    _panStartPointer,
                    _panStartOffset,
                    evt.localPosition,
                    _zoom,
                    viewportSize,
                    textureSize,
                    out _panStartPointer,
                    out _panStartOffset);
                ApplyTransform();
                evt.StopPropagation();
            });

            _viewport.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!_isPanning)
                {
                    return;
                }

                _isPanning = false;
                _viewport.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });

            _viewport.RegisterCallback<PointerCaptureOutEvent>(_ => _isPanning = false);
            _viewport.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                SyncImageToViewport();

                if (_overlay?.style.display != DisplayStyle.Flex)
                {
                    return;
                }

                ReclampPan();
            });
        }

        private void ApplyTransform()
        {
            if (_image == null)
            {
                return;
            }

            _image.style.scale = new Scale(new Vector2(_zoom, _zoom));
            _image.style.translate = new Translate(_pan.x, _pan.y);
        }

        private void ResetTransform()
        {
            _zoom = 1f;
            _pan = Vector2.zero;
            _isPanning = false;
            SyncImageToViewport();
            ReclampPan();
        }

        private void ReclampPan()
        {
            if (!TryGetGeometry(out var viewportSize, out var textureSize))
            {
                _pan = Vector2.zero;
                ApplyTransform();
                return;
            }

            _pan = ImageOverlayGeometry.ClampPan(_pan, _zoom, viewportSize, textureSize);
            ApplyTransform();
        }

        private bool TryGetGeometry(out Vector2 viewportSize, out Vector2 textureSize)
        {
            viewportSize = GetViewportSize();
            textureSize = GetTextureSize();
            return viewportSize.x > 0f && viewportSize.y > 0f && textureSize.x > 0f && textureSize.y > 0f;
        }

        private Vector2 GetViewportSize()
        {
            if (_viewport == null)
            {
                return Vector2.zero;
            }

            float width = _viewport.resolvedStyle.width;
            float height = _viewport.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return Vector2.zero;
            }

            return new Vector2(width, height);
        }

        private Vector2 GetTextureSize()
        {
            var texture = _image?.image as Texture2D;
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return Vector2.zero;
            }

            return new Vector2(texture.width, texture.height);
        }

        private void SyncImageToViewport()
        {
            Vector2 viewportSize = GetViewportSize();
            if (_image == null || viewportSize.x <= 0f || viewportSize.y <= 0f)
            {
                return;
            }

            _image.style.width = viewportSize.x;
            _image.style.height = viewportSize.y;
        }
    }
}
