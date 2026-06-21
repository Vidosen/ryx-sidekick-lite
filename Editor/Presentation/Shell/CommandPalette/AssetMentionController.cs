// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Domain.Commands;
using Ryx.Sidekick.Editor.Domain.Parsing;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell.CommandPalette
{
    /// <summary>
    /// Section key for asset mention results.
    /// </summary>
    internal static class MentionSections
    {
        public const string Assets = "Assets";
    }

    /// <summary>
    /// Controller for @ asset mention overlay.
    /// Uses the same CommandPaletteView as slash commands for consistent UX.
    /// </summary>
    internal sealed class AssetMentionController : IDisposable
    {
        private readonly CommandPaletteView _view;
        private readonly TextField _inputField;
        private readonly AttachmentController _attachmentController;
        private readonly VisualElement _inputWrapper;

        private bool _isOpen;
        private AtDetectionResult _currentDetection;
        private IVisualElementScheduledItem _debounceSchedule;
        private string _lastQuery = "";

        private const int DebounceMs = 100;
        private const float OverlayBottomPadding = 8f; // Gap between overlay and input wrapper

        /// <summary>
        /// Fired when an asset is selected and attached.
        /// Provides the asset path for text insertion.
        /// </summary>
        public event Action<string> OnAssetSelected;

        /// <summary>
        /// Whether the mention overlay is currently open.
        /// </summary>
        public bool IsOpen => _isOpen;

        /// <summary>
        /// The overlay view element.
        /// </summary>
        public CommandPaletteView View => _view;

        public AssetMentionController(TextField inputField, VisualElement paletteParent, AttachmentController attachmentController)
        {
            _inputField = inputField;
            _attachmentController = attachmentController;
            
            // Find input area for dynamic positioning (includes wrapper + padding)
            _inputWrapper = paletteParent?.Q<VisualElement>(className: "sk-input-area");

            _view = new CommandPaletteView();
            _view.AddToClassList("sk-asset-mention-palette");
            paletteParent?.Add(_view);

            // Wire view events
            _view.OnActionExecute += HandleActionExecute;
            _view.OnActionInsert += HandleActionInsert;
            _view.OnCloseRequested += Close;
            
            // Listen for input area geometry changes to reposition overlay
            if (_inputWrapper != null)
            {
                _inputWrapper.RegisterCallback<GeometryChangedEvent>(OnInputWrapperGeometryChanged);
            }
        }

        public void Dispose()
        {
            _view.OnActionExecute -= HandleActionExecute;
            _view.OnActionInsert -= HandleActionInsert;
            _view.OnCloseRequested -= Close;
            
            if (_inputWrapper != null)
            {
                _inputWrapper.UnregisterCallback<GeometryChangedEvent>(OnInputWrapperGeometryChanged);
            }
            
            _debounceSchedule?.Pause();
            _debounceSchedule = null;
        }

        /// <summary>
        /// Updates the overlay based on current input field state.
        /// Called on input text change.
        /// </summary>
        public void UpdateFromInput(string text, int caretIndex)
        {
            if (!_isOpen && AtDetector.ShouldOpenMentions(text, caretIndex))
            {
                var detection = AtDetector.Detect(text, caretIndex);
                if (detection.IsActive)
                {
                    Open(detection);
                    return;
                }
            }

            if (_isOpen)
            {
                var detection = AtDetector.Detect(text, caretIndex);
                if (!detection.IsActive)
                {
                    // User moved caret away from @ token
                    Close();
                    return;
                }

                // Update search with new query (debounced)
                _currentDetection = detection;
                ScheduleSearch(detection.Query);
            }
        }

        /// <summary>
        /// Opens the overlay for @ mention trigger.
        /// </summary>
        private void Open(AtDetectionResult detection)
        {
            _currentDetection = detection;
            _isOpen = true;
            _lastQuery = detection.Query;

            // Perform initial search
            var results = ProjectAssetSearchService.Search(detection.Query);
            var grouped = ConvertToGroupedActions(results);
            
            _view.Show(grouped, showFilter: false, initialFilter: detection.Query);
            UpdateOverlayPosition();
        }

        /// <summary>
        /// Closes the overlay.
        /// </summary>
        public void Close()
        {
            _isOpen = false;
            _currentDetection = AtDetectionResult.None;
            _lastQuery = "";
            _debounceSchedule?.Pause();
            _view.Hide();
            _inputField?.Focus();
        }

        /// <summary>
        /// Handles keyboard events from the input field.
        /// Returns true if the event was consumed.
        /// </summary>
        public bool HandleKeyDown(KeyDownEvent evt)
        {
            if (!_isOpen) return false;

            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    Close();
                    return true;

                case KeyCode.UpArrow:
                    _view.SelectPrevious();
                    return true;

                case KeyCode.DownArrow:
                    _view.SelectNext();
                    return true;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Tab:
                    var selected = _view.GetSelectedAction();
                    if (selected != null)
                    {
                        HandleActionExecute(selected);
                        return true;
                    }
                    break;
            }

            return false;
        }

        /// <summary>
        /// Schedules a debounced search.
        /// </summary>
        private void ScheduleSearch(string query)
        {
            if (query == _lastQuery) return;
            _lastQuery = query;

            _debounceSchedule?.Pause();
            _debounceSchedule = _view.schedule.Execute(() =>
            {
                if (!_isOpen) return;

                var results = ProjectAssetSearchService.Search(query);
                var grouped = ConvertToGroupedActions(results);
                _view.UpdateList(grouped);
            }).StartingIn(DebounceMs);
        }

        /// <summary>
        /// Converts search results to CommandAction groups for the view.
        /// </summary>
        private static IReadOnlyList<(string Section, IReadOnlyList<CommandAction> Actions)> ConvertToGroupedActions(List<AssetSearchResult> results)
        {
            if (results == null || results.Count == 0)
            {
                return Array.Empty<(string, IReadOnlyList<CommandAction>)>();
            }

            var actions = results.Select(r => new CommandAction($"asset:{r.AssetPath}", r.DisplayName, MentionSections.Assets)
            {
                Description = GetShortPath(r.AssetPath),
                TrailingVisual = r.IsPrefab ? "prefab" : GetExtension(r.FileName),
                SupportsInsert = true,
                InsertText = r.AssetPath
            }).ToList();

            return new List<(string, IReadOnlyList<CommandAction>)>
            {
                (MentionSections.Assets, actions)
            };
        }

        /// <summary>
        /// Gets a shortened path for display.
        /// </summary>
        private static string GetShortPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            
            // Remove filename
            var lastSlash = path.LastIndexOf('/');
            if (lastSlash <= 0) return "";
            
            var dir = path.Substring(0, lastSlash);
            
            // Truncate if too long
            if (dir.Length > 40)
            {
                return "..." + dir.Substring(dir.Length - 37);
            }
            return dir;
        }

        /// <summary>
        /// Gets file extension without dot.
        /// </summary>
        private static string GetExtension(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            
            var dot = fileName.LastIndexOf('.');
            if (dot < 0 || dot >= fileName.Length - 1) return "";
            
            return fileName.Substring(dot + 1).ToLowerInvariant();
        }

        /// <summary>
        /// Handles asset selection via Enter/click.
        /// </summary>
        private void HandleActionExecute(CommandAction action)
        {
            if (action == null) return;

            var assetPath = action.InsertText;
            if (string.IsNullOrEmpty(assetPath)) return;

            // Add as context attachment
            AddAssetAsAttachment(assetPath);

            // Replace @ token with full mention in input
            if (_currentDetection.IsActive && _inputField != null)
            {
                var (newText, newCaret) = MentionTextTransform.ReplaceToken(
                    _inputField.value,
                    _currentDetection,
                    assetPath);

                // Schedule the value update after event processing
                var capturedText = newText;
                var capturedCaret = newCaret;
                _inputField.schedule.Execute(() =>
                {
                    if (_inputField != null)
                    {
                        _inputField.value = capturedText;
                        _inputField.cursorIndex = capturedCaret;
                        _inputField.selectIndex = capturedCaret;
                    }
                });
            }

            // Notify listeners
            OnAssetSelected?.Invoke(assetPath);

            Close();
        }

        /// <summary>
        /// Handles Tab insert (same as execute for mentions).
        /// </summary>
        private void HandleActionInsert(CommandAction action)
        {
            HandleActionExecute(action);
        }

        /// <summary>
        /// Adds the asset as a context attachment.
        /// </summary>
        private void AddAssetAsAttachment(string assetPath)
        {
            if (_attachmentController == null) return;

            if (assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                // Load prefab and add as GameObject context
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab != null)
                {
                    _attachmentController.TryAddGameObjectContext(prefab);
                }
            }
            else
            {
                // Add as file context
                _attachmentController.TryAddFileContext(assetPath);
            }
        }

        private void OnInputWrapperGeometryChanged(GeometryChangedEvent evt)
        {
            if (_isOpen)
            {
                UpdateOverlayPosition();
            }
        }

        /// <summary>
        /// Updates the overlay position based on input area height.
        /// </summary>
        private void UpdateOverlayPosition()
        {
            if (_inputWrapper == null || _view == null) return;

            var inputAreaHeight = _inputWrapper.resolvedStyle.height;
            if (float.IsNaN(inputAreaHeight) || inputAreaHeight <= 0)
            {
                // Fallback to default (typical input area height)
                inputAreaHeight = 100f;
            }

            var bottomOffset = inputAreaHeight + OverlayBottomPadding;
            _view.style.bottom = bottomOffset;
        }
    }
}
