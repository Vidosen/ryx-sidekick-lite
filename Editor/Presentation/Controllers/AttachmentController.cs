// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ryx.Sidekick.Editor.UseCases.Attachments;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using Ryx.Sidekick.Editor.Presentation.Presenters;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Controllers
{
    internal sealed class AttachmentController : IComposerAttachmentSource, IAttachmentInteractions, IImageTextureResolver
    {
        private readonly IAttachmentElementFactory _factory;
        private readonly IClipboardService _clipboardService;
        private readonly IDragDropAttachmentSource _dragDropSource;
        private readonly IViewScreenshotService _screenshotService;
        private readonly AttachmentSessionState _state;
        private readonly AddImageAttachmentUseCase _addImage;
        private readonly AddContextAttachmentUseCase _addContext;
        private readonly RemoveAttachmentUseCase _remove;
        private readonly Dictionary<string, Texture2D> _textureCache = new();

        internal AttachmentController(
            IAttachmentElementFactory factory,
            IViewScreenshotService screenshotService,
            IClipboardService clipboardService = null,
            IDragDropAttachmentSource dragDropSource = null,
            AttachmentSessionState state = null,
            AddImageAttachmentUseCase addImage = null,
            AddContextAttachmentUseCase addContext = null,
            RemoveAttachmentUseCase remove = null)
        {
            _factory = factory;
            _clipboardService = clipboardService;
            _dragDropSource = dragDropSource;
            _screenshotService = screenshotService;

            _state = state ?? new AttachmentSessionState();
            _addImage = addImage ?? new AddImageAttachmentUseCase(_state);
            _addContext = addContext ?? new AddContextAttachmentUseCase(_state);
            _remove = remove ?? new RemoveAttachmentUseCase(_state);

            // Forward state changes to the controller's Changed event before BindViews runs.
            _state.Changed += () => Changed?.Invoke();
        }

        private IComposerView _composerView;
        private IStatusBarView _statusBarView;
        private ImageOverlayPresenter _imageOverlayPresenter;

        public event Action Changed;

        public IReadOnlyList<ImageAttachment> PendingAttachments => _state.PendingImages;
        public IReadOnlyList<IContextAttachment> PendingContextAttachments => _state.PendingContexts;

        IReadOnlyList<ImageAttachment> IComposerAttachmentSource.Pending => _state.PendingImages;
        IReadOnlyList<IContextAttachment> IComposerAttachmentSource.Context => _state.PendingContexts;

        /// <summary>
        /// Checks if there are any pending attachments (images or context).
        /// </summary>
        public bool HasPendingAttachments()
        {
            return _state.HasPending;
        }

        public void BindViews(
            IComposerView composerView,
            IImageOverlayView imageOverlayView,
            IStatusBarView statusBarView)
        {
            if (_composerView != null)
            {
                _composerView.AttachmentPreviewOpened -= HandleAttachmentPreviewOpened;
                _composerView.AttachmentPreviewRemoved -= RemovePendingAttachment;
                _composerView.ContextAttachmentOpened -= HandleContextAttachmentOpened;
                _composerView.ContextAttachmentRemoved -= RemoveContextAttachment;
            }

            // Dispose the existing presenter before rebinding (unsubscribes ClosedRequested).
            _imageOverlayPresenter?.Dispose();
            _imageOverlayPresenter = null;

            _composerView = composerView;
            _statusBarView = statusBarView;

            if (_composerView != null)
            {
                _composerView.AttachmentPreviewOpened += HandleAttachmentPreviewOpened;
                _composerView.AttachmentPreviewRemoved += RemovePendingAttachment;
                _composerView.ContextAttachmentOpened += HandleContextAttachmentOpened;
                _composerView.ContextAttachmentRemoved += RemoveContextAttachment;
            }

            if (imageOverlayView != null)
            {
                _imageOverlayPresenter = new ImageOverlayPresenter(imageOverlayView, _state, this);
            }

            UpdatePendingAttachmentsPreview();
            UpdateContextChipsUI();
        }

        private void HandleAttachmentPreviewOpened(string attachmentId)
        {
            _imageOverlayPresenter?.OpenForPendingImage(attachmentId);
        }

        private void HandleContextAttachmentOpened(string attachmentId)
        {
            if (string.IsNullOrEmpty(attachmentId))
            {
                return;
            }

            var attachment = _state.FindContextById(attachmentId);
            if (attachment is ScreenshotContextAttachment screenshot)
            {
                _imageOverlayPresenter?.OpenForPendingScreenshot(screenshot);
                return;
            }

            if (attachment != null)
            {
                ContextChipElement.HandleContextChipClick(attachment);
            }
        }

        public void ClearPendingAttachments(bool destroyTextures = true)
        {
            if (destroyTextures)
            {
                foreach (var attachment in _state.PendingImages)
                {
                    var id = attachment?.Id;
                    if (string.IsNullOrEmpty(id)) continue;

                    if (_textureCache.TryGetValue(id, out var tex) && tex != null)
                    {
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                    _textureCache.Remove(id);
                }
            }

            _textureCache.Clear();
            _state.Clear();
            UpdatePendingAttachmentsPreview();
            UpdateContextChipsUI();
            UpdateContextIndicator();
        }

        /// <summary>
        /// Restores pending attachments from persisted state (e.g., after domain reload).
        /// </summary>
        public void RestorePendingAttachments(
            IEnumerable<ImageAttachment> images,
            IEnumerable<IContextAttachment> contexts)
        {
            _textureCache.Clear();
            _state.Restore(images, contexts);

            UpdatePendingAttachmentsPreview();
            UpdateContextChipsUI();
            UpdateContextIndicator();
        }

        public bool TryPasteImageFromClipboard()
        {
            if (TryAddImageAttachmentFromClipboardText())
            {
                return true;
            }

            if (_clipboardService != null && _clipboardService.TryReadImagePng(out var pngBytes))
            {
                return TryAddImageAttachmentFromPngBytes(pngBytes);
            }

            return false;
        }

        private bool TryAddImageAttachmentFromPngBytes(byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0) return false;

            if (!TryLoadTexture(pngBytes, out var tex))
            {
                return false;
            }

            var attachment = new ImageAttachment
            {
                MediaType = "image/png",
                Data = Convert.ToBase64String(pngBytes),
                FileName = "clipboard.png"
            };

            AddPendingAttachment(attachment, tex);
            return true;
        }

        public bool HasDraggedImage()
        {
            return _dragDropSource?.HasImageDrag() ?? false;
        }

        public bool TryAddImageAttachmentFromTexture(Texture2D texture, string fileName)
        {
            if (!texture) return false;

            try
            {
                var pngBytes = _screenshotService.EncodePngFromTexture(texture);
                if (pngBytes == null || pngBytes.Length == 0) return false;

                if (!TryLoadTexture(pngBytes, out var preview))
                {
                    return false;
                }

                var attachment = new ImageAttachment
                {
                    MediaType = "image/png",
                    Data = Convert.ToBase64String(pngBytes),
                    FileName = string.IsNullOrEmpty(fileName) ? "image.png" : fileName
                };

                AddPendingAttachment(attachment, preview);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryAddImageAttachmentFromPathOrFileUrl(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            string path = null;

            if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    path = new Uri(text).LocalPath;
                }
                catch
                {
                    path = null;
                }
            }
            else
            {
                path = text;
            }

            if (string.IsNullOrEmpty(path)) return false;

            path = path.Trim().Trim('"');

            if (!File.Exists(path)) return false;

            var mediaType = AttachmentUtils.GetMediaTypeFromPath(path);
            if (string.IsNullOrEmpty(mediaType) || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                if (!TryLoadTexture(bytes, out var tex))
                {
                    return false;
                }

                var attachment = new ImageAttachment
                {
                    MediaType = mediaType,
                    Data = Convert.ToBase64String(bytes),
                    FileName = Path.GetFileName(path)
                };

                AddPendingAttachment(attachment, tex);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void AddAttachmentsToContent(VisualElement content, Message message)
        {
            if (content == null) return;

            var existing = content.Q<VisualElement>("sk-context-attachments");
            if (existing != null)
            {
                content.Remove(existing);
            }

            var ctxContainer = _factory.CreateContextAttachmentsContainer(
                message?.ContextAttachments, message, this);
            if (ctxContainer != null)
            {
                var insertIndex = 0;
                var thinkingContainer = content.Q<VisualElement>(name: "thinking-container");
                if (thinkingContainer != null)
                {
                    insertIndex = content.IndexOf(thinkingContainer) + 1;
                }
                content.Insert(insertIndex, ctxContainer);
            }

            var existingImages = content.Q<VisualElement>("sk-attachments");
            if (existingImages != null)
            {
                content.Remove(existingImages);
            }

            var imgContainer = _factory.CreateImageAttachmentsContainer(message?.Attachments, this);
            if (imgContainer != null)
            {
                content.Add(imgContainer);
            }
        }

        // ===================================================================
        // IAttachmentInteractions
        // ===================================================================

        void IAttachmentInteractions.OpenContextAttachment(IContextAttachment attachment, Message message)
        {
            if (attachment is ScreenshotContextAttachment screenshot)
                _imageOverlayPresenter?.OpenForMessageScreenshot(screenshot, message);
            else
                ContextChipElement.HandleContextChipClick(attachment);
        }

        void IAttachmentInteractions.OpenImagePreview(Texture2D texture)
        {
            _imageOverlayPresenter?.OpenForTexture(texture);
        }

        Texture2D IAttachmentInteractions.ResolveImageTexture(ImageAttachment attachment)
        {
            return GetAttachmentTexture(attachment);
        }

        // ===================================================================
        // IImageTextureResolver
        // ===================================================================

        Texture2D IImageTextureResolver.Resolve(ImageAttachment attachment)
        {
            return GetAttachmentTexture(attachment);
        }

        private void AddPendingAttachment(ImageAttachment attachment, Texture2D previewTexture = null)
        {
            if (attachment == null) return;

            var result = _addImage.Execute(attachment);
            if (!result.Added) return;

            if (previewTexture)
            {
                previewTexture.hideFlags = HideFlags.HideAndDontSave;
                _textureCache[result.Attachment.Id] = previewTexture;
            }

            UpdatePendingAttachmentsPreview();
            UpdateContextIndicator();
        }

        private void RemovePendingAttachment(string attachmentId)
        {
            if (string.IsNullOrEmpty(attachmentId)) return;

            var result = _remove.RemoveImage(attachmentId);
            if (!result.Removed) return;

            if (_textureCache.TryGetValue(attachmentId, out var tex) && tex)
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
            _textureCache.Remove(attachmentId);

            if (result.RemovedContext != null)
            {
                UpdateContextChipsUI();
            }

            UpdatePendingAttachmentsPreview();
            UpdateContextIndicator();
        }

        private void UpdatePendingAttachmentsPreview()
        {
            // Changed is already forwarded from _state.Changed in ctor; no double-fire needed here.
            if (_composerView == null)
            {
                return;
            }

            var previews = _state.PendingImages
                .Where(attachment => attachment != null)
                .Select(attachment => new ComposerAttachmentPreviewItem(
                    attachment.Id,
                    GetAttachmentTexture(attachment)))
                .ToList();

            _composerView.RenderAttachmentPreviews(previews);
        }

        private Texture2D GetAttachmentTexture(ImageAttachment attachment)
        {
            if (attachment == null || string.IsNullOrEmpty(attachment.Data)) return null;

            var cacheKey = !string.IsNullOrEmpty(attachment.Id)
                ? attachment.Id
                : attachment.Data.GetHashCode().ToString();

            if (_textureCache.TryGetValue(cacheKey, out var cached) && cached != null)
            {
                return cached;
            }

            try
            {
                var bytes = Convert.FromBase64String(attachment.Data);
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes))
                {
                    _textureCache[cacheKey] = tex;
                    return tex;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static bool TryLoadTexture(byte[] bytes, out Texture2D texture)
        {
            texture = null;
            if (bytes == null || bytes.Length == 0) return false;

            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                if (tex.LoadImage(bytes))
                {
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    texture = tex;
                    return true;
                }

                UnityEngine.Object.DestroyImmediate(tex);
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TryAddImageAttachmentFromClipboardText()
        {
            var text = _clipboardService?.Text;
            if (string.IsNullOrWhiteSpace(text)) return false;

            text = text.Trim();

            if (TryAddImageAttachmentFromDataUrl(text))
            {
                return true;
            }

            if (TryAddImageAttachmentFromPathOrFileUrl(text))
            {
                return true;
            }

            return false;
        }

        private bool TryAddImageAttachmentFromDataUrl(string dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl)) return false;
            if (!dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return false;

            var comma = dataUrl.IndexOf(',');
            if (comma < 0 || comma >= dataUrl.Length - 1) return false;

            var meta = dataUrl.Substring(5, comma - 5);
            var mediaType = meta.Split(';')[0];
            if (string.IsNullOrEmpty(mediaType)) mediaType = "image/png";

            var base64 = dataUrl.Substring(comma + 1).Trim();
            try
            {
                var bytes = Convert.FromBase64String(base64);
                if (!TryLoadTexture(bytes, out var tex))
                {
                    return false;
                }

                var attachment = new ImageAttachment
                {
                    MediaType = mediaType,
                    Data = Convert.ToBase64String(bytes),
                    FileName = AttachmentUtils.GetDefaultFileNameForMediaType(mediaType)
                };

                AddPendingAttachment(attachment, tex);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ===================================================================
        // Context Attachments (Files, GameObjects)
        // ===================================================================

        /// <summary>
        /// Checks if any dragged items include context-compatible objects (GameObjects, text files).
        /// </summary>
        public bool HasDraggedContext()
        {
            return _dragDropSource?.HasContextDrag() ?? false;
        }

        /// <summary>
        /// Consumes the current drag payload: adds all image and context attachments from
        /// <see cref="IDragDropAttachmentSource"/>. Returns the relative/asset paths of any
        /// context attachments added, so the caller can insert mentions at the caret.
        /// Returns an empty list when no drag source is registered.
        /// </summary>
        public IReadOnlyList<string> ConsumeDrag()
        {
            if (_dragDropSource == null) return System.Array.Empty<string>();

            var addedContextPaths = new List<string>();

            // File paths from OS (and Unity asset paths)
            foreach (var path in _dragDropSource.GetContextPaths())
            {
                // Try as image first
                if (TryAddImageAttachmentFromPathOrFileUrl(path))
                    continue;

                // Try as context file
                if (TryAddFileContext(path))
                {
                    var relativePath = AssetLinkService.ToRelativePath(path);
                    if (!string.IsNullOrEmpty(relativePath))
                        addedContextPaths.Add(relativePath);
                }
            }

            // Object references dragged from Project view or Scene hierarchy.
            // Asset paths are resolved by the infrastructure adapter (DraggedContextObject.AssetPath)
            // so this controller remains free of UnityEditor API dependencies.
            foreach (var item in _dragDropSource.GetContextObjects())
            {
                var obj = item.Reference;
                var assetPath = item.AssetPath;

                if (obj is Texture2D tex)
                {
                    TryAddImageAttachmentFromTexture(tex, tex.name + ".png");
                }
                else if (obj is GameObject go)
                {
                    if (TryAddGameObjectContext(go))
                    {
                        if (!string.IsNullOrEmpty(assetPath))
                            addedContextPaths.Add(assetPath);
                    }
                }
                else if (obj != null)
                {
                    if (!string.IsNullOrEmpty(assetPath) && TryAddFileContext(assetPath))
                        addedContextPaths.Add(assetPath);
                }
                else if (!string.IsNullOrEmpty(assetPath))
                {
                    // Reference is null (e.g. skipped by adapter) but the asset path is known —
                    // fall back to file-context lookup so path-only items are not silently dropped.
                    if (TryAddFileContext(assetPath))
                        addedContextPaths.Add(assetPath);
                }
            }

            return addedContextPaths;
        }

        /// <summary>
        /// Adds a file as context attachment.
        /// </summary>
        public bool TryAddFileContext(string assetPath)
        {
            var attachment = ContextAttachmentService.CreateFromAssetPath(assetPath);
            if (attachment == null)
                return false;

            if (!_addContext.Execute(attachment))
            {
                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[AttachmentController] File already added: {attachment.FilePath}");
                }
                return false;
            }

            UpdateContextChipsUI();
            return true;
        }

        /// <summary>
        /// Adds a GameObject as context attachment.
        /// </summary>
        public bool TryAddGameObjectContext(GameObject go)
        {
            var attachment = ContextAttachmentService.CreateFromGameObject(go);
            if (attachment == null)
                return false;

            if (!_addContext.Execute(attachment))
            {
                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[AttachmentController] GameObject already added: {attachment.ObjectName}");
                }
                return false;
            }

            UpdateContextChipsUI();
            return true;
        }

        /// <summary>
        /// Removes a context attachment by ID.
        /// Also removes linked image attachment for screenshots.
        /// </summary>
        public void RemoveContextAttachment(string id)
        {
            var result = _remove.RemoveContext(id);
            if (!result.Removed) return;

            // If a screenshot's linked image was removed, destroy its cached texture.
            if (result.RemovedImage != null)
            {
                var linkedImageId = result.RemovedImage.Id;
                if (_textureCache.TryGetValue(linkedImageId, out var tex) && tex)
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
                _textureCache.Remove(linkedImageId);

                UpdatePendingAttachmentsPreview();
            }

            UpdateContextChipsUI();
        }

        /// <summary>
        /// Adds a screenshot as a unified context item (metadata + linked image).
        /// </summary>
        /// <param name="kind">Whether this is a Scene View or Game View screenshot.</param>
        /// <param name="texture">The captured screenshot texture. Will be encoded to PNG.</param>
        /// <param name="meta">Camera metadata (only used for SceneView screenshots).</param>
        /// <returns>True if the screenshot was added successfully.</returns>
        public bool TryAddScreenshotContext(ScreenshotKind kind, Texture2D texture, SceneCameraMeta? meta)
        {
            if (texture == null) return false;

            try
            {
                // Encode texture to PNG
                var pngBytes = _screenshotService.EncodePngFromTexture(texture);
                if (pngBytes == null || pngBytes.Length == 0) return false;

                // Create unique IDs
                var contextId = Guid.NewGuid().ToString("N");
                var imageId = Guid.NewGuid().ToString("N");

                // Create screenshot context attachment with metadata
                var screenshotAttachment = new ScreenshotContextAttachment
                {
                    Id = contextId,
                    Kind = kind,
                    Width = texture.width,
                    Height = texture.height,
                    Timestamp = DateTime.Now,
                    LinkedImageAttachmentId = imageId
                };

                // Add camera metadata for Scene View
                if (kind == ScreenshotKind.SceneView && meta.HasValue)
                {
                    var m = meta.Value;
                    screenshotAttachment.CameraPosition = m.Position;
                    screenshotAttachment.CameraRotation = m.Rotation;
                    screenshotAttachment.IsOrthographic = m.IsOrthographic;
                    screenshotAttachment.FieldOfView = m.FieldOfView;
                    screenshotAttachment.OrthographicSize = m.OrthographicSize;
                    screenshotAttachment.NearClipPlane = m.NearClipPlane;
                    screenshotAttachment.FarClipPlane = m.FarClipPlane;
                }

                // Create linked image attachment
                var imageAttachment = new ImageAttachment
                {
                    Id = imageId,
                    MediaType = "image/png",
                    Data = Convert.ToBase64String(pngBytes),
                    FileName = kind == ScreenshotKind.SceneView ? "scene-view.png" : "game-view.png",
                    LinkedContextAttachmentId = contextId
                };

                // Create preview texture for the image cache
                var previewTexture = new Texture2D(texture.width, texture.height, texture.format, false);
                previewTexture.SetPixels(texture.GetPixels());
                previewTexture.Apply();
                previewTexture.hideFlags = HideFlags.HideAndDontSave;
                _textureCache[imageId] = previewTexture;

                // Add both to state via use cases (screenshot context is always appended, no dedup)
                _addContext.Execute(screenshotAttachment);
                _addImage.Execute(imageAttachment);

                UpdatePendingAttachmentsPreview();
                UpdateContextChipsUI();

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AttachmentController] Failed to add screenshot: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Updates the context chips UI to reflect the current pending context attachments.
        /// </summary>
        private void UpdateContextChipsUI()
        {
            if (_composerView == null)
                return;

            if (_state.PendingContexts.Count == 0)
            {
                _composerView.RenderContextAttachments(Array.Empty<ComposerContextAttachmentItem>());
                UpdateContextIndicator();
                return;
            }

            _composerView.RenderContextAttachments(
                _state.PendingContexts
                    .Where(attachment => attachment != null)
                    .Select(attachment => new ComposerContextAttachmentItem(attachment))
                    .ToList());

            UpdateContextIndicator();
        }

        /// <summary>
        /// Updates the context indicator in the toolbar (counts both context attachments and images).
        /// Does not double-count linked screenshot images.
        /// </summary>
        private void UpdateContextIndicator()
        {
            if (_statusBarView == null)
                return;

            // Count context attachments + unlinked images (linked screenshot images are counted via their context attachment)
            var unlinkedImageCount = _state.PendingImages.Count(a => string.IsNullOrEmpty(a?.LinkedContextAttachmentId));
            var count = _state.PendingContexts.Count + unlinkedImageCount;

            if (count > 0)
            {
                _statusBarView.SetContextStatus($"{count} item{(count > 1 ? "s" : "")}", IndicatorState.Success);
            }
            else
            {
                _statusBarView.SetContextStatus("Context", IndicatorState.Neutral);
            }
        }
    }
}
