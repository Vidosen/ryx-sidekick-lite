// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using System.Text;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    internal sealed class ComposerInputPresenter : IDisposable
    {
        private readonly SidekickWindowView _view;
        private readonly AttachmentController _attachmentController;
        private readonly ProviderSelectorViewModel _providerSelectorViewModel;
        private readonly ComposerContextAttachmentPresenter _contextAttachmentPresenter;
        private readonly Func<bool> _isCommandPaletteOpen;
        private readonly Action _createNewConversation;
        private ComposerViewModel _composerViewModel;
        private TextField _inputField;
        private VisualElement _inputWrapper;
        private TextElement _inputTextElement;
        private bool _disposed;

        public ComposerInputPresenter(
            SidekickWindowView view,
            AttachmentController attachmentController,
            ProviderSelectorViewModel providerSelectorViewModel,
            ComposerContextAttachmentPresenter contextAttachmentPresenter,
            Func<bool> isCommandPaletteOpen,
            Action createNewConversation)
        {
            _view = view;
            _attachmentController = attachmentController;
            _providerSelectorViewModel = providerSelectorViewModel;
            _contextAttachmentPresenter = contextAttachmentPresenter;
            _isCommandPaletteOpen = isCommandPaletteOpen ?? (() => false);
            _createNewConversation = createNewConversation;

            RegisterViewHandlers();
        }

        public void RebindProviderScope(ComposerViewModel composerViewModel)
        {
            _composerViewModel = composerViewModel;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_view?.Composer != null)
            {
                _view.Composer.NewChatRequested -= HandleNewChatRequested;
            }

            if (_inputField != null)
            {
                _inputField.UnregisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);
                _inputField.UnregisterCallback<ChangeEvent<string>>(HandleInputChanged);
                _inputField.UnregisterCallback<GeometryChangedEvent>(HandleInputGeometryChanged);
                _inputField.UnregisterCallback<FocusInEvent>(HandleFocusIn);
                _inputField.UnregisterCallback<FocusOutEvent>(HandleFocusOut);
            }

            _inputTextElement?.UnregisterCallback<GeometryChangedEvent>(HandleInputTextGeometryChanged);

            if (_inputWrapper != null)
            {
                _inputWrapper.UnregisterCallback<DragEnterEvent>(HandleDragEnter);
                _inputWrapper.UnregisterCallback<DragLeaveEvent>(HandleDragLeave);
                _inputWrapper.UnregisterCallback<DragExitedEvent>(HandleDragExited);
                _inputWrapper.UnregisterCallback<DragUpdatedEvent>(HandleDragUpdated);
                _inputWrapper.UnregisterCallback<DragPerformEvent>(HandleDragPerform);
                _inputWrapper.UnregisterCallback<DragPerformEvent>(HandleDragPerformFocus);
            }

            _inputField = null;
            _inputWrapper = null;
            _inputTextElement = null;
        }

        private void RegisterViewHandlers()
        {
            if (_view?.Composer != null)
            {
                _view.Composer.NewChatRequested += HandleNewChatRequested;
            }

            _inputField = _view?.InputField;
            _inputWrapper = _view?.InputWrapper;
            _inputTextElement = _inputField?.Q<TextElement>();
            if (_inputField == null)
            {
                return;
            }

            _inputField.RegisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);
            _inputField.RegisterCallback<ChangeEvent<string>>(HandleInputChanged);
            _inputField.RegisterCallback<GeometryChangedEvent>(HandleInputGeometryChanged);
            _inputTextElement?.RegisterCallback<GeometryChangedEvent>(HandleInputTextGeometryChanged);
            _inputField.RegisterCallback<FocusInEvent>(HandleFocusIn);
            _inputField.RegisterCallback<FocusOutEvent>(HandleFocusOut);
            _inputWrapper?.RegisterCallback<DragEnterEvent>(HandleDragEnter);
            _inputWrapper?.RegisterCallback<DragLeaveEvent>(HandleDragLeave);
            _inputWrapper?.RegisterCallback<DragExitedEvent>(HandleDragExited);
            _inputWrapper?.RegisterCallback<DragUpdatedEvent>(HandleDragUpdated);
            _inputWrapper?.RegisterCallback<DragPerformEvent>(HandleDragPerform);
            _inputWrapper?.RegisterCallback<DragPerformEvent>(HandleDragPerformFocus);
        }

        private void HandleNewChatRequested()
        {
            _createNewConversation?.Invoke();
        }

        private void HandleInputChanged(ChangeEvent<string> evt)
        {
            _view.Composer?.AdjustForContent();
        }

        private void HandleInputGeometryChanged(GeometryChangedEvent evt)
        {
            _view.Composer?.AdjustForContent();
        }

        private void HandleInputTextGeometryChanged(GeometryChangedEvent evt)
        {
            _view.Composer?.AdjustForContent();
        }

        private void HandleFocusIn(FocusInEvent evt)
        {
            _inputWrapper?.AddToClassList("sk-input-wrapper--focus");
        }

        private void HandleFocusOut(FocusOutEvent evt)
        {
            _inputWrapper?.RemoveFromClassList("sk-input-wrapper--focus");
        }

        private void HandleDragEnter(DragEnterEvent evt)
        {
            if (_attachmentController?.HasDraggedImage() == true || _attachmentController?.HasDraggedContext() == true)
            {
                _inputWrapper?.AddToClassList("sk-input-wrapper--drag-over");
            }
        }

        private void HandleDragLeave(DragLeaveEvent evt)
        {
            _inputWrapper?.RemoveFromClassList("sk-input-wrapper--drag-over");
        }

        private void HandleDragExited(DragExitedEvent evt)
        {
            _inputWrapper?.RemoveFromClassList("sk-input-wrapper--drag-over");
        }

        private void HandleDragUpdated(DragUpdatedEvent evt)
        {
            if (_attachmentController?.HasDraggedImage() != true && _attachmentController?.HasDraggedContext() != true)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (_inputWrapper?.ClassListContains("sk-input-wrapper--drag-over") == false)
            {
                _inputWrapper.AddToClassList("sk-input-wrapper--drag-over");
            }

            evt.StopPropagation();
        }

        private void HandleDragPerform(DragPerformEvent evt)
        {
            _inputWrapper?.RemoveFromClassList("sk-input-wrapper--drag-over");

            var hasImage = _attachmentController?.HasDraggedImage() == true;
            var hasContext = _attachmentController?.HasDraggedContext() == true;

            if (!hasImage && !hasContext)
            {
                return;
            }

            DragAndDrop.AcceptDrag();

            var pendingBefore = (_attachmentController?.PendingAttachments.Count ?? 0)
                              + (_attachmentController?.PendingContextAttachments.Count ?? 0);

            var addedContextPaths = _attachmentController?.ConsumeDrag() ?? Array.Empty<string>();

            var pendingAfter = (_attachmentController?.PendingAttachments.Count ?? 0)
                             + (_attachmentController?.PendingContextAttachments.Count ?? 0);

            if (addedContextPaths.Count > 0)
            {
                _contextAttachmentPresenter?.InsertMultipleAssetMentionsAtCaret(addedContextPaths.ToArray());
            }

            if (pendingAfter > pendingBefore)
            {
                evt.StopPropagation();
            }
        }

        private void HandleDragPerformFocus(DragPerformEvent evt)
        {
            _inputField?.Focus();
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            var inputField = _view?.InputField;
            if (inputField == null)
            {
                return;
            }

            var isEnter = evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter;

            if ((evt.commandKey || evt.ctrlKey || evt.actionKey) && evt.keyCode == KeyCode.V)
            {
                if (_attachmentController?.TryPasteImageFromClipboard() == true)
                {
                    evt.StopImmediatePropagation();
                }
            }
            else if (evt.keyCode == KeyCode.Tab)
            {
                evt.StopImmediatePropagation();
                _providerSelectorViewModel?.CyclePrimaryModeCommand.Execute(null);
            }
            else if (isEnter && (evt.commandKey || evt.ctrlKey || evt.actionKey || evt.shiftKey || evt.altKey))
            {
                evt.StopImmediatePropagation();
                InsertNewLineAtCursor(inputField);
            }
            else if (isEnter)
            {
                if (_isCommandPaletteOpen())
                {
                    return;
                }

                evt.StopImmediatePropagation();
                _composerViewModel?.RequestSendCommand.Execute(null);

                var field = inputField;
                EditorApplication.delayCall += () =>
                {
                    if (!_disposed && field != null && string.IsNullOrWhiteSpace(field.value))
                    {
                        field.value = string.Empty;
                    }
                };
            }
        }

        private void InsertNewLineAtCursor(TextField field)
        {
            if (field == null)
            {
                return;
            }

            var text = field.value ?? string.Empty;
            var start = Mathf.Min(field.cursorIndex, field.selectIndex);
            var end = Mathf.Max(field.cursorIndex, field.selectIndex);
            start = Mathf.Clamp(start, 0, text.Length);
            end = Mathf.Clamp(end, 0, text.Length);

            var builder = new StringBuilder(text.Length + 1);
            builder.Append(text, 0, start);
            builder.Append('\n');
            builder.Append(text, end, text.Length - end);

            field.value = builder.ToString();

            var caret = start + 1;
            field.cursorIndex = caret;
            field.selectIndex = caret;
            field.Focus();

            _view?.Composer?.AdjustForContent();
        }
    }
}
