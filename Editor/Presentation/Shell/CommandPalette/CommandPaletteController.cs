// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Commands;
using Ryx.Sidekick.Editor.Domain.Parsing;
using Ryx.Sidekick.Editor.UseCases.Commands;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell.CommandPalette
{
    /// <summary>
    /// Controller for the command palette.
    /// Manages state, detection, filtering, and action dispatch.
    /// </summary>
    internal sealed class CommandPaletteController : IDisposable
    {
        private readonly CommandRegistry _registry;
        private readonly CommandPaletteView _view;
        private readonly TextField _inputField;
        private readonly VisualElement _inputWrapper;

        private bool _isOpen;
        private bool _isSlashTriggered;
        private SlashDetectionResult _currentDetection;

        private const float OverlayBottomPadding = 8f; // Gap between overlay and input wrapper

        /// <summary>
        /// Fired when a slash command is executed (sent to chat).
        /// </summary>
        public event Action<string> OnSlashCommandExecute;

        /// <summary>
        /// Whether the palette is currently open.
        /// </summary>
        public bool IsOpen => _isOpen;

        /// <summary>
        /// The palette view element.
        /// </summary>
        public CommandPaletteView View => _view;

        public CommandPaletteController(CommandRegistry registry, TextField inputField, VisualElement paletteParent)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _inputField = inputField;
            
            // Find input area for dynamic positioning (includes wrapper + padding)
            _inputWrapper = paletteParent?.Q<VisualElement>(className: "sk-input-area");

            _view = new CommandPaletteView();
            paletteParent?.Add(_view);

            // Wire view events
            _view.OnActionExecute += HandleActionExecute;
            _view.OnActionInsert += HandleActionInsert;
            _view.OnFilterChanged += HandleFilterChanged;
            _view.OnCloseRequested += Close;

            // Wire registry changes
            _registry.OnRegistryChanged += HandleRegistryChanged;
            
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
            _view.OnFilterChanged -= HandleFilterChanged;
            _view.OnCloseRequested -= Close;
            _registry.OnRegistryChanged -= HandleRegistryChanged;
            
            if (_inputWrapper != null)
            {
                _inputWrapper.UnregisterCallback<GeometryChangedEvent>(OnInputWrapperGeometryChanged);
            }
        }

        /// <summary>
        /// Opens the palette in general mode (show all sections, filter field visible).
        /// </summary>
        public void OpenGeneral()
        {
            _isSlashTriggered = false;
            _currentDetection = SlashDetectionResult.None;

            var grouped = _registry.GetGroupedActions();
            _view.Show(grouped, showFilter: true, initialFilter: "");
            _isOpen = true;
            UpdateOverlayPosition();
        }

        /// <summary>
        /// Opens the palette in slash-trigger mode (filter by query, no filter field).
        /// </summary>
        public void OpenSlashTrigger(SlashDetectionResult detection)
        {
            _isSlashTriggered = true;
            _currentDetection = detection;

            // Filter only slash commands by the query
            var grouped = _registry.GetGroupedActions(detection.Query);
            Debug.Log($"[CommandPalette] OpenSlashTrigger - grouped actions count: {grouped?.Count ?? 0}");
            foreach (var g in grouped ?? System.Array.Empty<(string, System.Collections.Generic.IReadOnlyList<CommandAction>)>())
            {
                Debug.Log($"[CommandPalette]   Section '{g.Section}': {g.Actions?.Count ?? 0} actions");
            }
            _view.Show(grouped, showFilter: false, initialFilter: detection.Query);
            _isOpen = true;
            UpdateOverlayPosition();
        }

        /// <summary>
        /// Closes the palette.
        /// </summary>
        public void Close()
        {
            _isOpen = false;
            _isSlashTriggered = false;
            _currentDetection = SlashDetectionResult.None;
            _view.Hide();
            _inputField?.Focus();
        }

        /// <summary>
        /// Updates the palette based on current input field state.
        /// Called on input text change.
        /// </summary>
        public void UpdateFromInput(string text, int caretIndex)
        {
            if (!_isOpen && SlashDetector.ShouldOpenPalette(text, caretIndex))
            {
                Debug.Log($"[CommandPalette] Slash detected - text: '{text}', caret: {caretIndex}");
                var detection = SlashDetector.Detect(text, caretIndex);
                if (detection.IsActive)
                {
                    Debug.Log($"[CommandPalette] Opening slash trigger - query: '{detection.Query}'");
                    OpenSlashTrigger(detection);
                    return;
                }
            }

            if (_isOpen && _isSlashTriggered)
            {
                var detection = SlashDetector.Detect(text, caretIndex);
                if (!detection.IsActive)
                {
                    // User moved caret away from slash token
                    Close();
                    return;
                }

                // Update filter with new query
                _currentDetection = detection;
                var grouped = _registry.GetGroupedActions(detection.Query);
                _view.UpdateList(grouped);
            }
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
                    var selected = _view.GetSelectedAction();
                    if (selected != null)
                    {
                        HandleActionExecute(selected);
                        return true;
                    }
                    break;

                case KeyCode.Tab:
                    var selectedForInsert = _view.GetSelectedAction();
                    if (selectedForInsert != null && selectedForInsert.SupportsInsert)
                    {
                        HandleActionInsert(selectedForInsert);
                        return true;
                    }
                    break;
            }

            return false;
        }

        private void HandleActionExecute(CommandAction action)
        {
            if (action == null) return;

            // Pro-locked actions open the paywall instead of executing — preserve input text as-is.
            if (action.IsProLocked)
            {
                action.OnExecute?.Invoke();
                Close();
                return;
            }

            // Remove the slash token from input for any action executed via slash trigger
            if (_isSlashTriggered && _currentDetection.IsActive && _inputField != null)
            {
                var (newText, newCaret) = SlashTextTransform.RemoveToken(_inputField.value, _currentDetection);
                
                // CRITICAL: TextField's internal Enter handling may insert newline despite StopImmediatePropagation.
                // We schedule the value restoration to run AFTER the current event processing cycle completes.
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

            // For slash commands, execute means send to chat
            if (action.Id.StartsWith("slash:"))
            {
                OnSlashCommandExecute?.Invoke(action.InsertText ?? action.Label);
                Close();
                return;
            }

            // For other actions, execute callback
            action.OnExecute?.Invoke();

            if (!action.KeepMenuOpen)
            {
                Close();
            }
        }

        private void HandleActionInsert(CommandAction action)
        {
            if (action == null || !action.SupportsInsert || _inputField == null) return;

            var insertText = action.InsertText ?? action.Label;

            if (_isSlashTriggered && _currentDetection.IsActive)
            {
                // Replace the partial slash token with the full command
                var (newText, newCaret) = SlashTextTransform.ReplaceToken(
                    _inputField.value,
                    _currentDetection,
                    insertText);

                _inputField.value = newText;
                _inputField.cursorIndex = newCaret;
                _inputField.selectIndex = newCaret;
            }
            else
            {
                // Insert at caret
                var (newText, newCaret) = SlashTextTransform.InsertCommand(
                    _inputField.value,
                    _inputField.cursorIndex,
                    insertText);

                _inputField.value = newText;
                _inputField.cursorIndex = newCaret;
                _inputField.selectIndex = newCaret;
            }

            Close();
        }

        private void HandleFilterChanged(string filter)
        {
            if (!_isOpen) return;

            // Update list with filtered actions
            var grouped = _registry.GetGroupedActions(filter);
            _view.UpdateList(grouped);
        }

        private void HandleRegistryChanged()
        {
            if (!_isOpen) return;

            // Refresh the list
            var query = _isSlashTriggered ? _currentDetection.Query : _view.FilterText;
            var grouped = _registry.GetGroupedActions(query);
            _view.UpdateList(grouped);
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

