// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    /// <summary>
    /// Metadata captured from Scene View camera during screenshot.
    /// </summary>
    internal struct SceneCameraMeta
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public bool IsOrthographic;
        public float FieldOfView;
        public float OrthographicSize;
        public float NearClipPlane;
        public float FarClipPlane;
    }

    /// <summary>
    /// Service for capturing screenshots from Scene View and Game View.
    /// </summary>
    internal static class ViewScreenshotService
    {
        private const int MaxSize = 2048;
        private const int MaxReadScreenPixelSize = 8192;

        private static float GetEditorPixelsPerPoint()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // On macOS Retina this is typically 2.0; on Windows usually 1.0.
                return Mathf.Clamp(EditorGUIUtility.pixelsPerPoint, 1f, 4f);
            }
            else
            {
                return 1.0f;
            }
        }

        /// <summary>
        /// Captures a screenshot from the active Scene View, including all gizmos and handles.
        /// </summary>
        /// <param name="texture">The captured texture (caller must destroy it).</param>
        /// <param name="meta">Camera metadata from the Scene View.</param>
        /// <param name="error">Error message if capture failed.</param>
        /// <returns>True if capture succeeded.</returns>
        public static bool TryCaptureSceneView(out Texture2D texture, out SceneCameraMeta meta, out string error)
        {
            texture = null;
            meta = default;
            error = null;

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                error = "No active Scene View found. Please open a Scene View window.";
                return false;
            }

            var camera = sceneView.camera;
            if (camera == null)
            {
                error = "Scene View camera not available.";
                return false;
            }

            // Capture camera metadata
            meta = new SceneCameraMeta
            {
                Position = camera.transform.position,
                Rotation = camera.transform.rotation,
                IsOrthographic = camera.orthographic,
                FieldOfView = camera.fieldOfView,
                OrthographicSize = camera.orthographicSize,
                NearClipPlane = camera.nearClipPlane,
                FarClipPlane = camera.farClipPlane
            };

            try
            {
                // Ensure SceneView is the active view. InternalEditorUtility.ReadScreenPixel is sensitive to focus.
                sceneView.Focus();
                var viewRect = sceneView.position;
                //if (viewRect.width <= 1f || viewRect.height <= 1f)
                //{
                //    viewRect = GetEditorWindowScreenRect(sceneView);
                //}

                // ReadScreenPixel* APIs are quirky across platforms/HiDPI. Empirically (and on mac Retina),
                // the "hint" position behaves in points while the returned pixel buffer size is in pixels.
                // So: keep hint in the same coordinate space as viewRect, but scale width/height by pixelsPerPoint.
                float pixelsPerPoint = GetEditorPixelsPerPoint();
                int rawWidth = Mathf.RoundToInt(viewRect.width * pixelsPerPoint);
                int rawHeight = Mathf.RoundToInt(viewRect.height * pixelsPerPoint);
                bool posIsCenter = Application.platform == RuntimePlatform.OSXEditor;
                Vector2 capturePosPrimary = posIsCenter ? viewRect.center : viewRect.position;
                Vector2 capturePosSecondary = posIsCenter ? viewRect.position : viewRect.center;

                // Guard against extreme allocations (HiDPI + large editor layouts).
                rawWidth = Mathf.Clamp(rawWidth, 1, MaxReadScreenPixelSize);
                rawHeight = Mathf.Clamp(rawHeight, 1, MaxReadScreenPixelSize);

                // Preferred path: capture pixels from the actual SceneView window buffer.
                // This includes gizmos/overlays exactly as displayed.
                try
                {
                    // Platform differences: on macOS this appears to interpret pixelPos as center; on Windows as top-left.
                    // We do a deterministic first attempt and a fallback attempt with swapped semantics.
                    var pixels = InternalEditorUtility.ReadScreenPixel(capturePosPrimary, rawWidth, rawHeight);
                    if (pixels == null || pixels.Length != rawWidth * rawHeight)
                    {
                        pixels = InternalEditorUtility.ReadScreenPixel(capturePosSecondary, rawWidth, rawHeight);
                    }

                    if (pixels == null || pixels.Length != rawWidth * rawHeight)
                    {
                        throw new Exception($"ReadScreenPixel returned invalid buffer (len={pixels?.Length ?? 0}, expected={rawWidth * rawHeight}).");
                    }

                    var captured = new Texture2D(rawWidth, rawHeight, TextureFormat.RGB24, false);
                    captured.SetPixels(pixels);
                    captured.Apply();

                    // Downscale to MaxSize without cropping.
                    int width = rawWidth;
                    int height = rawHeight;
                    if (width > MaxSize || height > MaxSize)
                    {
                        ClampDimensions(ref width, ref height, MaxSize);
                        texture = ResizeTexture(captured, width, height);
                        UnityEngine.Object.DestroyImmediate(captured);
                    }
                    else
                    {
                        texture = captured;
                    }

                    return true;
                }
                catch
                {
                    // Fall back to offscreen render (may exclude gizmos).
                }

                // Fallback: render directly through camera (no gizmos in most cases)
                int fallbackWidth = rawWidth;
                int fallbackHeight = rawHeight;
                ClampDimensions(ref fallbackWidth, ref fallbackHeight, MaxSize);

                var rt = RenderTexture.GetTemporary(fallbackWidth, fallbackHeight, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);

                try
                {
                    var previousRT = camera.targetTexture;
                    var previousActive = RenderTexture.active;

                    camera.targetTexture = rt;
                    camera.Render();
                    camera.targetTexture = previousRT;

                    RenderTexture.active = rt;

                    texture = new Texture2D(fallbackWidth, fallbackHeight, TextureFormat.RGB24, false);
                    texture.ReadPixels(new Rect(0, 0, fallbackWidth, fallbackHeight), 0, 0);
                    texture.Apply();

                    RenderTexture.active = previousActive;
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(rt);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to capture Scene View: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Captures a screenshot from the Game View.
        /// Works in both edit mode and play mode.
        /// </summary>
        /// <param name="texture">The captured texture (caller must destroy it).</param>
        /// <param name="error">Error message if capture failed.</param>
        /// <returns>True if capture succeeded.</returns>
        public static bool TryCaptureGameView(out Texture2D texture, out string error)
        {
            texture = null;
            error = null;

            // Find Game View window
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
            {
                error = "Could not find GameView type.";
                return false;
            }

            var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
            if (gameView == null)
            {
                error = "No Game View window found. Please open a Game View window.";
                return false;
            }

            try
            {
                // In play mode, we can use ScreenCapture
                if (Application.isPlaying)
                {
                    var captured = ScreenCapture.CaptureScreenshotAsTexture();
                    if (captured != null)
                    {
                        int w = captured.width;
                        int h = captured.height;
                        if (w > MaxSize || h > MaxSize)
                        {
                            ClampDimensions(ref w, ref h, MaxSize);
                            texture = ResizeTexture(captured, w, h);
                            UnityEngine.Object.DestroyImmediate(captured);
                        }
                        else
                        {
                            texture = captured;
                        }
                        return true;
                    }
                }

                // In edit mode (or if play mode capture failed), capture via render texture
                // Use reflection to get the Game View's target display and size
                var targetSizeProp = gameViewType.GetProperty("targetSize",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                Vector2 targetSize;
                if (targetSizeProp != null)
                {
                    targetSize = (Vector2)targetSizeProp.GetValue(gameView);
                }
                else
                {
                    // Fallback to window size
                    var rect = gameView.position;
                    targetSize = new Vector2(rect.width, rect.height);
                }

                int width = Mathf.FloorToInt(targetSize.x);
                int height = Mathf.FloorToInt(targetSize.y);

                if (width <= 0 || height <= 0)
                {
                    error = "Game View has invalid size. Please ensure the Game View is visible.";
                    return false;
                }

                ClampDimensions(ref width, ref height, MaxSize);

                // Find the main camera to render from
                var camera = Camera.main;
                if (camera == null)
                {
                    // Try to find any camera
                    camera = UnityEngine.Object.FindFirstObjectByType<Camera>();
                }

                if (camera == null)
                {
                    error = "No camera found in scene. Please add a camera to capture Game View.";
                    return false;
                }

                // Create render texture and capture
                var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 1;

                var previousRT = camera.targetTexture;
                var previousActive = RenderTexture.active;

                camera.targetTexture = rt;
                camera.Render();
                camera.targetTexture = previousRT;

                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();

                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to capture Game View: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Clamps dimensions to max size while preserving aspect ratio.
        /// </summary>
        private static void ClampDimensions(ref int width, ref int height, int maxSize)
        {
            if (width <= maxSize && height <= maxSize)
                return;

            float aspect = (float)width / height;
            if (width > height)
            {
                width = maxSize;
                height = Mathf.RoundToInt(maxSize / aspect);
            }
            else
            {
                height = maxSize;
                width = Mathf.RoundToInt(maxSize * aspect);
            }

            // Ensure minimum size
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
        }

        /// <summary>
        /// Resizes a texture using GPU blitting.
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int newWidth, int newHeight)
        {
            var rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;

            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;

            Graphics.Blit(source, rt);

            var result = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            result.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }
    }
}

