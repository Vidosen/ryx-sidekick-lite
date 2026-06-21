// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Chat;
using Unity.AppUI.MVVM;
using Unity.Properties;
using IDisposableSubscription = Unity.AppUI.Redux.IDisposableSubscription;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    internal sealed class ComposerSendIntent
    {
        public string Text { get; set; }
        public IReadOnlyList<ImageAttachment> Attachments { get; set; }
        public IReadOnlyList<IContextAttachment> ContextAttachments { get; set; }
    }

    [ObservableObject]
    internal partial class ComposerViewModel : IDisposable
    {
        private readonly SidekickStoreService _store;
        private readonly ChatSessionState _chatSession;
        private readonly IComposerAttachmentSource _attachmentSource;
        private readonly IEditorSelectionService _selectionService;
        private readonly IDisposableSubscription _turnSubscription;

        private IComposerView _view;
        private IAttachmentMenuView _attachmentMenuView;
        private string _draftText = string.Empty;
        private ComposerButtonMode _buttonMode = ComposerButtonMode.Send;
        private bool _isSendEnabled;
        private bool _isStopEnabled;
        private bool _isPromptEnabled = true;
        private bool _isAddContextEnabled = true;
        private bool _isCompactEnabled;
        private int _pendingAttachmentCount;
        private int _pendingContextAttachmentCount;
        private bool _isTurnActive;
        private bool _isStopping;
        private bool _disposed;

        // === Observable properties ===

        [CreateProperty]
        public string DraftText
        {
            get => _draftText;
            private set => SetProperty(ref _draftText, value);
        }

        [CreateProperty]
        public ComposerButtonMode ButtonMode
        {
            get => _buttonMode;
            private set => SetProperty(ref _buttonMode, value);
        }

        [CreateProperty]
        public bool IsSendEnabled
        {
            get => _isSendEnabled;
            private set => SetProperty(ref _isSendEnabled, value);
        }

        [CreateProperty]
        public bool IsStopEnabled
        {
            get => _isStopEnabled;
            private set => SetProperty(ref _isStopEnabled, value);
        }

        [CreateProperty]
        public bool IsPromptEnabled
        {
            get => _isPromptEnabled;
            private set => SetProperty(ref _isPromptEnabled, value);
        }

        [CreateProperty]
        public bool IsAddContextEnabled
        {
            get => _isAddContextEnabled;
            private set => SetProperty(ref _isAddContextEnabled, value);
        }

        [CreateProperty]
        public bool IsCompactEnabled
        {
            get => _isCompactEnabled;
            private set => SetProperty(ref _isCompactEnabled, value);
        }

        [CreateProperty]
        public int PendingAttachmentCount
        {
            get => _pendingAttachmentCount;
            private set => SetProperty(ref _pendingAttachmentCount, value);
        }

        [CreateProperty]
        public int PendingContextAttachmentCount
        {
            get => _pendingContextAttachmentCount;
            private set => SetProperty(ref _pendingContextAttachmentCount, value);
        }

        private bool _isAttachmentMenuOpen;

        [CreateProperty]
        public bool IsAttachmentMenuOpen
        {
            get => _isAttachmentMenuOpen;
            private set => SetProperty(ref _isAttachmentMenuOpen, value);
        }

        private IReadOnlyList<AttachmentMenuItemViewState> _attachmentMenuItems =
            Array.Empty<AttachmentMenuItemViewState>();

        [CreateProperty]
        public IReadOnlyList<AttachmentMenuItemViewState> AttachmentMenuItems
        {
            get => _attachmentMenuItems;
            private set => SetProperty(ref _attachmentMenuItems, value);
        }

        // === Outgoing notifications (to host / ChatController) ===

        public event global::System.Action<ComposerSendIntent> SendRequested;
        public event global::System.Action<ComposerSendIntent> StopRequested;
        public event global::System.Action CompactRequested;
        public event global::System.Action<string> AttachmentMenuItemActivated;

        // === External input gate ===

        /// <summary>
        /// Directly enables or disables the composer input controls.
        /// Used by overlay ViewModels (<see cref="PermissionOverlayViewModel"/>,
        /// <see cref="AskUserQuestionViewModel"/>) to lock the composer while an overlay
        /// is shown. Does not participate in the VM's reactive flow — the next
        /// <c>RecomputeAndPublish</c> call will re-derive <c>IsPromptEnabled</c>
        /// from session state and restore normal behavior.
        /// </summary>
        public virtual void SetInputEnabled(bool enabled)
        {
            if (_view == null) return;
            _view.IsPromptEnabled = enabled;
            _view.IsSendButtonEnabled = enabled;
        }

        // === Constructor ===

        public ComposerViewModel(
            SidekickStoreService store,
            ChatSessionState chatSession,
            IComposerAttachmentSource attachmentSource,
            IEditorSelectionService selectionService)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _chatSession = chatSession ?? throw new ArgumentNullException(nameof(chatSession));
            _attachmentSource = attachmentSource ?? throw new ArgumentNullException(nameof(attachmentSource));
            _selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));

            _turnSubscription = _store.SubscribeToTurn(OnTurnChanged, fireImmediately: true);
            _attachmentSource.Changed += OnAttachmentsChanged;
            _chatSession.Changed += OnSessionChanged;
            _selectionService.SelectionChanged += OnSelectionChanged;

            RebuildAttachmentMenuItems();
        }

        // === Commands ===

        [ICommand]
        private void RequestSend()
        {
            if (ButtonMode == ComposerButtonMode.Stop)
            {
                StopRequested?.Invoke(BuildIntentFromCurrentState());
                return;
            }

            if (!IsSendEnabled)
            {
                return;
            }

            SendRequested?.Invoke(BuildIntentFromCurrentState());
        }

        [ICommand]
        private void RequestCompact()
        {
            if (!IsCompactEnabled)
            {
                return;
            }

            CompactRequested?.Invoke();
        }

        [ICommand]
        private void ToggleAttachmentMenu()
        {
            if (IsAttachmentMenuOpen)
            {
                IsAttachmentMenuOpen = false;
            }
            else
            {
                RebuildAttachmentMenuItems();
                IsAttachmentMenuOpen = true;
            }
        }

        [ICommand]
        private void CloseAttachmentMenu()
        {
            IsAttachmentMenuOpen = false;
        }

        [ICommand]
        private void SelectAttachmentMenuItem(string optionId)
        {
            if (string.IsNullOrEmpty(optionId))
            {
                return;
            }

            CloseAttachmentMenu();
            AttachmentMenuItemActivated?.Invoke(optionId);
        }

        // === View binding ===

        public void BindView(IComposerView view)
        {
            // Detach old view
            if (_view != null)
            {
                PropertyChanged -= OnVmPropertyChanged;
                _view.PromptChanged -= OnPromptChanged;
                _view.SendRequested -= OnSendRequested;
                _view.AddContextRequested -= OnAddContextRequested;
            }

            _view = view;

            if (_view == null)
            {
                return;
            }

            PropertyChanged += OnVmPropertyChanged;

            // Initial flush
            OnVmPropertyChanged(this, new PropertyChangedEventArgs(null));

            _view.PromptChanged += OnPromptChanged;
            _view.SendRequested += OnSendRequested;
            _view.AddContextRequested += OnAddContextRequested;
        }

        public void BindAttachmentMenuView(IAttachmentMenuView attachmentMenuView)
        {
            if (_attachmentMenuView != null)
            {
                _attachmentMenuView.PopupDismissed -= OnAttachmentMenuPopupDismissed;
                _attachmentMenuView.AttachmentMenuItemSelected -= OnAttachmentMenuItemSelected;
            }

            _attachmentMenuView = attachmentMenuView;

            if (_attachmentMenuView == null)
            {
                return;
            }

            _attachmentMenuView.PopupDismissed += OnAttachmentMenuPopupDismissed;
            _attachmentMenuView.AttachmentMenuItemSelected += OnAttachmentMenuItemSelected;

            // Initial flush so the popup picks up current items list immediately.
            _attachmentMenuView.RenderAttachmentMenuItems(AttachmentMenuItems);
        }

        // === IDisposable ===

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _turnSubscription?.Dispose();
            _attachmentSource.Changed -= OnAttachmentsChanged;
            _chatSession.Changed -= OnSessionChanged;
            _selectionService.SelectionChanged -= OnSelectionChanged;

            if (_view != null)
            {
                PropertyChanged -= OnVmPropertyChanged;
                _view.PromptChanged -= OnPromptChanged;
                _view.SendRequested -= OnSendRequested;
                _view.AddContextRequested -= OnAddContextRequested;
                _view = null;
            }

            if (_attachmentMenuView != null)
            {
                _attachmentMenuView.PopupDismissed -= OnAttachmentMenuPopupDismissed;
                _attachmentMenuView.AttachmentMenuItemSelected -= OnAttachmentMenuItemSelected;
                _attachmentMenuView = null;
            }
        }

        // === Private helpers ===

        private ComposerSendIntent BuildIntentFromCurrentState()
        {
            var pendingCount = _attachmentSource.Pending?.Count ?? 0;
            var contextCount = _attachmentSource.Context?.Count ?? 0;
            var hasContent = !string.IsNullOrWhiteSpace(_draftText) || pendingCount > 0 || contextCount > 0;
            if (!hasContent)
            {
                return null;
            }

            return new ComposerSendIntent
            {
                Text = _draftText,
                Attachments = pendingCount > 0
                    ? new List<ImageAttachment>(_attachmentSource.Pending)
                    : null,
                ContextAttachments = contextCount > 0
                    ? new List<IContextAttachment>(_attachmentSource.Context)
                    : null
            };
        }

        private void RecomputeAndPublish()
        {
            var pendingCount = _attachmentSource.Pending?.Count ?? 0;
            var contextCount = _attachmentSource.Context?.Count ?? 0;
            var hasContent = !string.IsNullOrWhiteSpace(_draftText) || pendingCount > 0 || contextCount > 0;

            var buttonMode = _isTurnActive ? ComposerButtonMode.Stop : ComposerButtonMode.Send;
            var isSendEnabled = !_isTurnActive && hasContent;
            var isStopEnabled = _isTurnActive && !_isStopping;
            var isConversationLoading = _chatSession.IsCurrentConversationLoading;
            var isPromptEnabled = !isConversationLoading;
            var isAddContextEnabled = !_isTurnActive;
            var hasMessages = (_chatSession.CurrentConversation?.Messages?.Count ?? 0) > 0;
            var isCompactEnabled = !_isTurnActive && hasMessages;

            ButtonMode = buttonMode;
            IsSendEnabled = isSendEnabled;
            IsStopEnabled = isStopEnabled;
            IsPromptEnabled = isPromptEnabled;
            IsAddContextEnabled = isAddContextEnabled;
            IsCompactEnabled = isCompactEnabled;
            PendingAttachmentCount = pendingCount;
            PendingContextAttachmentCount = contextCount;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var name = e.PropertyName;

            if (_view != null)
            {
                if (name is null or nameof(ButtonMode) or nameof(IsStopEnabled) or nameof(IsSendEnabled))
                {
                    _view.SetSendButtonMode(ButtonMode == ComposerButtonMode.Stop
                        ? ComposerSendButtonMode.Stop
                        : ComposerSendButtonMode.Send);
                    _view.IsSendButtonEnabled = ButtonMode == ComposerButtonMode.Stop
                        ? IsStopEnabled
                        : IsSendEnabled;
                }

                if (name is null or nameof(IsPromptEnabled))
                {
                    _view.IsPromptEnabled = IsPromptEnabled;
                }
            }

            if (_attachmentMenuView != null)
            {
                if (name is null or nameof(AttachmentMenuItems))
                {
                    _attachmentMenuView.RenderAttachmentMenuItems(AttachmentMenuItems);
                }

                if (name is null or nameof(IsAttachmentMenuOpen))
                {
                    _attachmentMenuView.ShowPopup(IsAttachmentMenuOpen);
                }
            }
        }

        private void OnTurnChanged(TurnState turnState)
        {
            _isTurnActive = turnState?.IsTurnActive ?? false;
            _isStopping = turnState?.IsStopping ?? false;
            RecomputeAndPublish();
        }

        private void OnAttachmentsChanged()
        {
            RecomputeAndPublish();
        }

        private void OnSessionChanged()
        {
            RecomputeAndPublish();
        }

        private void OnPromptChanged(string text)
        {
            DraftText = text ?? string.Empty;
            RecomputeAndPublish();
        }

        private void OnSendRequested()
        {
            RequestSendCommand.Execute(null);
        }

        private void OnAddContextRequested()
        {
            ToggleAttachmentMenuCommand.Execute(null);
        }

        private void OnAttachmentMenuPopupDismissed()
        {
            if (IsAttachmentMenuOpen)
            {
                CloseAttachmentMenuCommand.Execute(null);
            }
        }

        private void OnAttachmentMenuItemSelected(string optionId)
        {
            SelectAttachmentMenuItemCommand.Execute(optionId);
        }

        private void OnSelectionChanged()
        {
            RebuildAttachmentMenuItems();
        }

        private void RebuildAttachmentMenuItems()
        {
            var sel = _selectionService.Current;
            var items = new List<AttachmentMenuItemViewState>(4);

            if (sel.HasSelection)
            {
                string label = sel.Kind switch
                {
                    EditorSelectionKind.GameObject => $"Add GameObject: {sel.DisplayName}",
                    EditorSelectionKind.File => $"Add File: {sel.DisplayName}",
                    _ => $"Add Asset: {sel.DisplayName}",
                };
                items.Add(new AttachmentMenuItemViewState(
                    AttachmentMenuOptionIds.AddSelection,
                    label,
                    "cmd-selection",
                    isEnabled: true));
            }
            else
            {
                items.Add(new AttachmentMenuItemViewState(
                    AttachmentMenuOptionIds.AddSelection,
                    "(No selection)",
                    "cmd-selection",
                    isEnabled: false));
            }

            items.Add(new AttachmentMenuItemViewState(
                AttachmentMenuOptionIds.BrowseFiles,
                "Browse Project Files…",
                "tool-folder",
                isEnabled: true));

            items.Add(new AttachmentMenuItemViewState(
                AttachmentMenuOptionIds.ScreenshotSceneView,
                "Screenshot Scene View",
                "cmd-screenshot",
                isEnabled: true));

            items.Add(new AttachmentMenuItemViewState(
                AttachmentMenuOptionIds.ScreenshotGameView,
                "Screenshot Game View",
                "cmd-gameview",
                isEnabled: true));

            AttachmentMenuItems = items;
        }
    }
}
