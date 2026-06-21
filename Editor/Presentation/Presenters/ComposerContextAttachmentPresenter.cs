// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Parsing;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    internal sealed class ComposerContextAttachmentPresenter : IDisposable
    {
        private readonly AttachmentController _attachmentController;
        private readonly TextField _inputField;
        private ChatController _chatController;
        private ComposerViewModel _composerViewModel;
        private bool _disposed;

        public ComposerContextAttachmentPresenter(
            AttachmentController attachmentController,
            TextField inputField)
        {
            _attachmentController = attachmentController;
            _inputField = inputField;
        }

        public void RebindProviderScope(ChatController chatController, ComposerViewModel composerViewModel)
        {
            if (_composerViewModel != null)
            {
                _composerViewModel.AttachmentMenuItemActivated -= OnAttachmentMenuItemActivated;
            }

            _chatController = chatController;
            _composerViewModel = composerViewModel;

            if (_composerViewModel != null)
            {
                _composerViewModel.AttachmentMenuItemActivated += OnAttachmentMenuItemActivated;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_composerViewModel != null)
            {
                _composerViewModel.AttachmentMenuItemActivated -= OnAttachmentMenuItemActivated;
                _composerViewModel = null;
            }

            _chatController = null;
        }

        public void AddSelectionAsContext()
        {
            var obj = Selection.activeObject;
            if (obj == null)
            {
                return;
            }

            string assetPath = null;

            if (obj is GameObject go)
            {
                if (_attachmentController?.TryAddGameObjectContext(go) == true)
                {
                    assetPath = AssetDatabase.GetAssetPath(go);
                    if (string.IsNullOrEmpty(assetPath))
                    {
                        assetPath = null;
                    }
                }
            }
            else
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && _attachmentController?.TryAddFileContext(path) == true)
                {
                    assetPath = path;
                }
            }

            if (!string.IsNullOrEmpty(assetPath))
            {
                InsertAssetMentionAtCaret(assetPath);
            }
        }

        public void BrowseProjectFiles()
        {
            var path = EditorUtility.OpenFilePanel(
                "Select File to Add as Context",
                "Assets",
                "");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var relativePath = AssetLinkService.ToRelativePath(path);
            if (_attachmentController?.TryAddFileContext(path) == true && !string.IsNullOrEmpty(relativePath))
            {
                InsertAssetMentionAtCaret(relativePath);
            }
        }

        public void CaptureSceneViewScreenshot()
        {
            var sceneView = SceneView.lastActiveSceneView;
            sceneView?.Focus();
            sceneView?.Repaint();

            EditorApplication.delayCall += () =>
            {
                EditorApplication.delayCall += () =>
                {
                    if (!ViewScreenshotService.TryCaptureSceneView(out var texture, out var meta, out var error))
                    {
                        Debug.LogWarning($"[Ryx Sidekick] {error}");
                        _chatController?.ReportSystemError(error);
                        return;
                    }

                    var success = _attachmentController?.TryAddScreenshotContext(ScreenshotKind.SceneView, texture, meta) ?? false;

                    if (texture != null)
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                    }

                    if (success)
                    {
                        _inputField?.Focus();
                    }
                    else
                    {
                        Debug.LogWarning("[Ryx Sidekick] Failed to add Scene View screenshot to context.");
                        _chatController?.ReportSystemError("Failed to add Scene View screenshot to context.");
                    }
                };
            };
        }

        public void CaptureGameViewScreenshot()
        {
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType != null)
            {
                var gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                gameView?.Repaint();
            }

            EditorApplication.delayCall += () =>
            {
                if (!ViewScreenshotService.TryCaptureGameView(out var texture, out var error))
                {
                    Debug.LogWarning($"[Ryx Sidekick] {error}");
                    _chatController?.ReportSystemError(error);
                    return;
                }

                var success = _attachmentController?.TryAddScreenshotContext(ScreenshotKind.GameView, texture, null) ?? false;

                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                if (success)
                {
                    _inputField?.Focus();
                }
                else
                {
                    Debug.LogWarning("[Ryx Sidekick] Failed to add Game View screenshot to context.");
                    _chatController?.ReportSystemError("Failed to add Game View screenshot to context.");
                }
            };
        }

        public void InsertAssetMentionAtCaret(string assetPath)
        {
            if (_inputField == null || string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            var (newText, newCaret) = MentionTextTransform.InsertMention(
                _inputField.value,
                _inputField.cursorIndex,
                assetPath);

            _inputField.value = newText;
            _inputField.cursorIndex = newCaret;
            _inputField.selectIndex = newCaret;
            _inputField.Focus();
        }

        public void InsertMultipleAssetMentionsAtCaret(params string[] assetPaths)
        {
            if (_inputField == null || assetPaths == null || assetPaths.Length == 0)
            {
                return;
            }

            var validPaths = assetPaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (validPaths.Length == 0)
            {
                return;
            }

            var (newText, newCaret) = MentionTextTransform.InsertMultipleMentions(
                _inputField.value,
                _inputField.cursorIndex,
                validPaths);

            _inputField.value = newText;
            _inputField.cursorIndex = newCaret;
            _inputField.selectIndex = newCaret;
            _inputField.Focus();
        }

        private void OnAttachmentMenuItemActivated(string optionId)
        {
            switch (optionId)
            {
                case AttachmentMenuOptionIds.AddSelection:
                    AddSelectionAsContext();
                    break;
                case AttachmentMenuOptionIds.BrowseFiles:
                    BrowseProjectFiles();
                    break;
                case AttachmentMenuOptionIds.ScreenshotSceneView:
                    CaptureSceneViewScreenshot();
                    break;
                case AttachmentMenuOptionIds.ScreenshotGameView:
                    CaptureGameViewScreenshot();
                    break;
            }
        }
    }
}
