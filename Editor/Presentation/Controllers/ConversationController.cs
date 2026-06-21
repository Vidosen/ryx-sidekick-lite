// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Conversations;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Controllers
{
    internal sealed class ConversationControllerChatConversationSession : IChatConversationSession
    {
        private readonly ConversationController _conversationController;

        public event Action Changed;

        public ConversationControllerChatConversationSession(ConversationController conversationController)
        {
            _conversationController = conversationController;
            if (_conversationController != null)
            {
                _conversationController.CurrentConversationChanged += OnSessionStateChanged;
                _conversationController.HistoryLoadStatusChanged += OnSessionStateChanged;
            }
        }

        private void OnSessionStateChanged()
        {
            Changed?.Invoke();
        }

        public Conversation CurrentConversation => _conversationController?.CurrentConversation;

        public bool IsCurrentConversationLoading => _conversationController?.IsCurrentConversationLoading == true;

        public (Conversation conversation, bool created) EnsureConversation()
        {
            var created = false;

            if (_conversationController?.CurrentConversation == null)
            {
                _conversationController?.CreateNewConversation();
                created = true;
            }

            return (_conversationController?.CurrentConversation, created);
        }
    }

    internal sealed class ConversationController : IDisposable
    {
        private readonly LoadConversationListQuery _loadConversationListQuery;
        private readonly LoadConversationHistoryUseCase _loadConversationHistoryUseCase;
        private readonly SelectConversationUseCase _selectConversationUseCase;
        private readonly IPersistentSessionBackend _sessionBackend;
        private readonly string _providerDisplayName;
        private readonly ISettingsStore _settingsStore;
        private readonly Action<bool> _clearPendingAttachments;

        private IConversationMenuView _view;
        private int _listRequestVersion;
        private int _historyRequestVersion;
        private bool _disposed;

        public ConversationController(
            IConversationRepository conversationRepository,
            IPersistentSessionBackend sessionBackend,
            string providerDisplayName,
            ISettingsStore settingsStore,
            Action<bool> clearPendingAttachments = null)
            : this(
                new LoadConversationListQuery(conversationRepository, sessionBackend, new SystemClock(), providerDisplayName),
                new LoadConversationHistoryUseCase(conversationRepository, sessionBackend, providerDisplayName),
                new SelectConversationUseCase(
                    settingsStore,
                    new LoadConversationHistoryUseCase(conversationRepository, sessionBackend, providerDisplayName)),
                sessionBackend,
                providerDisplayName,
                settingsStore,
                clearPendingAttachments)
        {
        }

        public ConversationController(
            LoadConversationListQuery loadConversationListQuery,
            LoadConversationHistoryUseCase loadConversationHistoryUseCase,
            SelectConversationUseCase selectConversationUseCase,
            IPersistentSessionBackend sessionBackend,
            string providerDisplayName,
            ISettingsStore settingsStore,
            Action<bool> clearPendingAttachments = null)
        {
            _loadConversationListQuery = loadConversationListQuery ?? throw new ArgumentNullException(nameof(loadConversationListQuery));
            _loadConversationHistoryUseCase = loadConversationHistoryUseCase ?? throw new ArgumentNullException(nameof(loadConversationHistoryUseCase));
            _selectConversationUseCase = selectConversationUseCase ?? throw new ArgumentNullException(nameof(selectConversationUseCase));
            _sessionBackend = sessionBackend;
            _providerDisplayName = string.IsNullOrWhiteSpace(providerDisplayName) ? "Provider" : providerDisplayName;
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _clearPendingAttachments = clearPendingAttachments;

            if (_sessionBackend != null)
            {
                _sessionBackend.BootstrapStateChanged += HandleBootstrapStateChanged;
            }
        }

        public List<Conversation> Conversations { get; private set; } = new();

        private Conversation _currentConversation;
        public Conversation CurrentConversation
        {
            get => _currentConversation;
            private set
            {
                _currentConversation = value;
                CurrentConversationChanged?.Invoke();
            }
        }
        public bool IsPopupOpen { get; private set; }
        public ConversationLoadStatus<ConversationListLoadState> ListLoadStatus { get; private set; } =
            new(ConversationListLoadState.Idle);
        public ConversationLoadStatus<ConversationHistoryLoadState> HistoryLoadStatus { get; private set; } =
            new(ConversationHistoryLoadState.Idle);
        public bool IsCurrentConversationLoading => CurrentConversation?.IsHistoryLoading == true
            || HistoryLoadStatus.State is ConversationHistoryLoadState.Loading or ConversationHistoryLoadState.Initializing;

        /// <summary>
        /// Fired when conversation usage info is loaded from history (usedTokens, contextWindow).
        /// </summary>
        public event Action<int, int> OnConversationUsageLoaded;

        public event Action CurrentConversationChanged;

        public event Action HistoryLoadStatusChanged;

        private void SetConversationHistoryLoading(Conversation conversation, bool isLoading)
        {
            if (conversation == null)
            {
                return;
            }

            if (conversation.IsHistoryLoading == isLoading)
            {
                return;
            }

            conversation.IsHistoryLoading = isLoading;
            HistoryLoadStatusChanged?.Invoke();
        }

        public void BindView(IConversationMenuView view)
        {
            if (_view != null)
            {
                _view.SearchChanged -= FilterConversations;
                _view.RetryRequested -= RetryLoadFromPopup;
                _view.ConversationSelected -= HandleConversationSelected;
                _view.ConversationDeleteRequested -= DeleteConversation;
            }

            _view = view;
            if (_view != null)
            {
                _view.SearchChanged += FilterConversations;
                _view.RetryRequested += RetryLoadFromPopup;
                _view.ConversationSelected += HandleConversationSelected;
                _view.ConversationDeleteRequested += DeleteConversation;
            }

            UpdateConversationSearchState();
            PopulateConversationPopup(_view?.SearchText);
        }

        public void SetCurrentConversation(Conversation conversation)
        {
            CurrentConversation = conversation;
        }

        public Task LoadConversationsAsync()
        {
            return LoadSessionListAsync(
                restoreSavedConversation: true,
                preserveCurrentConversation: false,
                reloadCurrentConversation: true);
        }

        public void CreateNewConversation()
        {
            _sessionBackend?.DetachActiveConversationState();
            CurrentConversation = new Conversation();
            Conversations.RemoveAll(conversation => string.IsNullOrEmpty(conversation?.SessionId));
            Conversations.Insert(0, CurrentConversation);
            _clearPendingAttachments?.Invoke(true);
            SetHistoryStatus(ConversationHistoryLoadState.Empty);
            ApplyUsage(ConversationUsagePayload.None());
            PopulateConversationPopup(_view?.SearchText);
        }

        public async Task SelectConversationAsync(Conversation conversation)
        {
            if (_disposed || conversation == null)
            {
                return;
            }

            var requestVersion = ++_historyRequestVersion;
            CurrentConversation = conversation;
            _clearPendingAttachments?.Invoke(true);
            SetConversationHistoryLoading(conversation, !string.IsNullOrEmpty(conversation.SessionId));

            var result = await _selectConversationUseCase.ExecuteAsync(
                new SelectConversationRequest
                {
                    Conversation = conversation
                },
                progress => ApplyHistoryProgressIfCurrent(requestVersion, conversation, progress));

            if (!IsHistoryRequestCurrent(requestVersion, conversation))
            {
                return;
            }

            SetConversationHistoryLoading(conversation, false);
            if (result?.FinalHistoryStatus != null)
            {
                SetHistoryStatus(
                    result.FinalHistoryStatus.State,
                    result.FinalHistoryStatus.Message,
                    result.FinalHistoryStatus.CanRetry);
            }
        }

        public void DeleteConversation(Conversation conversation)
        {
            if (conversation == null)
            {
                return;
            }

            Conversations.Remove(conversation);
            if (CurrentConversation == conversation)
            {
                var nextConversation = Conversations.FirstOrDefault(item => !string.IsNullOrEmpty(item.SessionId))
                    ?? Conversations.FirstOrDefault();

                if (nextConversation != null)
                {
                    CurrentConversation = nextConversation;
                    if (!string.IsNullOrEmpty(nextConversation.SessionId))
                    {
                        _ = LoadConversationHistoryAsync(
                            nextConversation,
                            forceReload: nextConversation.Messages == null || nextConversation.Messages.Count == 0);
                    }
                    else
                    {
                        SetHistoryStatus(ConversationHistoryLoadState.Empty);
                        ApplyUsage(ConversationUsagePayload.None());
                    }
                }
                else
                {
                    CreateNewConversation();
                    return;
                }
            }

            PopulateConversationPopup(_view?.SearchText);
        }

        public Task RefreshOnFocusAsync()
        {
            return LoadSessionListAsync(
                restoreSavedConversation: false,
                preserveCurrentConversation: true,
                reloadCurrentConversation: true);
        }

        public void TogglePopup()
        {
            if (IsPopupOpen) ClosePopup();
            else _ = OpenPopupAsync();
        }

        public async Task OpenPopupAsync()
        {
            if (_view == null || _disposed)
            {
                return;
            }

            IsPopupOpen = true;
            PopulateConversationPopup(string.Empty);
            await LoadSessionListAsync(
                restoreSavedConversation: false,
                preserveCurrentConversation: true,
                reloadCurrentConversation: false);

            if (_view != null
                && ListLoadStatus.State != ConversationListLoadState.Loading
                && ListLoadStatus.State != ConversationListLoadState.Initializing)
            {
                _view.FocusSearch();
            }
        }

        public void ClosePopup()
        {
            IsPopupOpen = false;
            PopulateConversationPopup(_view?.SearchText);
        }

        public bool IsClickInsidePopup(ClickEvent evt)
        {
            return _view?.IsClickInside(evt) == true;
        }

        public void FilterConversations(string filter)
        {
            PopulateConversationPopup(filter);
        }

        public void Dispose()
        {
            _disposed = true;
            _listRequestVersion++;
            _historyRequestVersion++;
            if (_sessionBackend != null)
            {
                _sessionBackend.BootstrapStateChanged -= HandleBootstrapStateChanged;
            }

            _view = null;
        }

        private async Task LoadSessionListAsync(
            bool restoreSavedConversation,
            bool preserveCurrentConversation,
            bool reloadCurrentConversation)
        {
            if (_disposed)
            {
                return;
            }

            var requestVersion = ++_listRequestVersion;
            var currentConversation = CurrentConversation;
            var keepUnsavedCurrent = preserveCurrentConversation
                && currentConversation != null
                && string.IsNullOrEmpty(currentConversation.SessionId);
            Conversations = keepUnsavedCurrent
                ? new List<Conversation> { currentConversation }
                : new List<Conversation>();

            if (!keepUnsavedCurrent)
            {
                CurrentConversation = null;
            }

            var result = await _loadConversationListQuery.ExecuteAsync(
                new LoadConversationListRequest
                {
                    RestoreSavedConversation = restoreSavedConversation,
                    PreserveCurrentConversation = preserveCurrentConversation,
                    ReloadCurrentConversation = reloadCurrentConversation,
                    CurrentConversation = currentConversation,
                    CurrentHistoryStatus = HistoryLoadStatus,
                    LastOpenedSessionId = _settingsStore.LastOpenedSessionId
                },
                progress => ApplyListProgressIfCurrent(requestVersion, progress));

            if (!IsListRequestCurrent(requestVersion))
            {
                return;
            }

            Conversations = result?.Conversations ?? new List<Conversation>();
            CurrentConversation = result?.SelectedConversation;

            if (result?.FinalListStatus != null)
            {
                SetListStatus(
                    result.FinalListStatus.State,
                    result.FinalListStatus.Message,
                    result.FinalListStatus.CanRetry);
            }

            if (result?.FinalHistoryStatus != null)
            {
                SetHistoryStatus(
                    result.FinalHistoryStatus.State,
                    result.FinalHistoryStatus.Message,
                    result.FinalHistoryStatus.CanRetry);
            }

            if (result?.NextAction == ConversationLoadNextAction.EnsureEmptyConversation)
            {
                EnsureCurrentConversationExists();
            }
            else if (result?.NextAction == ConversationLoadNextAction.LoadSelectedHistory
                     && CurrentConversation != null
                     && !string.IsNullOrEmpty(CurrentConversation.SessionId))
            {
                var forceReload = reloadCurrentConversation
                    || CurrentConversation.Messages == null
                    || CurrentConversation.Messages.Count == 0;
                if (forceReload)
                {
                    CurrentConversation.Messages?.Clear();
                }

                await LoadConversationHistoryAsync(CurrentConversation, forceReload);
                return;
            }

            PopulateConversationPopup(_view?.SearchText);
        }

        private void EnsureCurrentConversationExists()
        {
            if (CurrentConversation != null)
            {
                return;
            }

            CurrentConversation = new Conversation();
            Conversations.RemoveAll(conversation => string.IsNullOrEmpty(conversation?.SessionId));
            Conversations.Insert(0, CurrentConversation);
        }

        private async Task LoadConversationHistoryAsync(Conversation conversation, bool forceReload)
        {
            if (_disposed || conversation == null || string.IsNullOrEmpty(conversation.SessionId))
            {
                SetHistoryStatus(ConversationHistoryLoadState.Empty);
                ApplyUsage(ConversationUsagePayload.None());
                return;
            }

            var requestVersion = ++_historyRequestVersion;
            SetConversationHistoryLoading(conversation, true);
            if (forceReload)
            {
                conversation.Messages?.Clear();
            }

            var result = await _loadConversationHistoryUseCase.ExecuteAsync(
                new LoadConversationHistoryRequest
                {
                    Conversation = conversation,
                    ForceReload = forceReload
                },
                progress => ApplyHistoryProgressIfCurrent(requestVersion, conversation, progress));

            if (!IsHistoryRequestCurrent(requestVersion, conversation))
            {
                return;
            }

            SetConversationHistoryLoading(conversation, false);
            if (result?.FinalHistoryStatus != null)
            {
                SetHistoryStatus(
                    result.FinalHistoryStatus.State,
                    result.FinalHistoryStatus.Message,
                    result.FinalHistoryStatus.CanRetry);
            }
        }

        private void ApplyListProgressIfCurrent(int requestVersion, ConversationLoadProgress progress)
        {
            if (!IsListRequestCurrent(requestVersion) || progress == null)
            {
                return;
            }

            ApplyProgress(progress, refreshPopup: true);
        }

        private void ApplyHistoryProgressIfCurrent(int requestVersion, Conversation conversation, ConversationLoadProgress progress)
        {
            if (!IsHistoryRequestCurrent(requestVersion, conversation) || progress == null)
            {
                return;
            }

            ApplyProgress(progress, refreshPopup: false);
        }

        private void ApplyProgress(ConversationLoadProgress progress, bool refreshPopup)
        {
            if (progress.ListStatus != null)
            {
                SetListStatus(
                    progress.ListStatus.State,
                    progress.ListStatus.Message,
                    progress.ListStatus.CanRetry);
            }

            if (progress.HistoryStatus != null)
            {
                SetHistoryStatus(
                    progress.HistoryStatus.State,
                    progress.HistoryStatus.Message,
                    progress.HistoryStatus.CanRetry);
            }

            if (progress.Usage != null)
            {
                ApplyUsage(progress.Usage);
            }

            if (progress.ShouldRefreshUi)
            {
                if (refreshPopup)
                {
                    PopulateConversationPopup(_view?.SearchText);
                }
            }
        }

        private void ApplyUsage(ConversationUsagePayload usage)
        {
            if (usage == null)
            {
                return;
            }

            if (usage.HasUsage)
            {
                OnConversationUsageLoaded?.Invoke(usage.TotalTokens, usage.ContextWindow);
            }
            else
            {
                OnConversationUsageLoaded?.Invoke(0, 0);
            }
        }

        private void HandleBootstrapStateChanged(PersistentSessionBootstrapState state)
        {
            if (_disposed)
            {
                return;
            }

            if (ListLoadStatus.State == ConversationListLoadState.Initializing
                || HistoryLoadStatus.State == ConversationHistoryLoadState.Initializing)
            {
                PopulateConversationPopup(_view?.SearchText);
            }
        }

        private bool IsListRequestCurrent(int requestVersion)
        {
            return !_disposed && requestVersion == _listRequestVersion;
        }

        private bool IsHistoryRequestCurrent(int requestVersion, Conversation conversation)
        {
            return !_disposed
                && requestVersion == _historyRequestVersion
                && ReferenceEquals(CurrentConversation, conversation);
        }

        private void SetListStatus(ConversationListLoadState state, string message = null, bool canRetry = false)
        {
            ListLoadStatus = new ConversationLoadStatus<ConversationListLoadState>(state, message, canRetry);
            UpdateConversationSearchState();
        }

        private void SetHistoryStatus(ConversationHistoryLoadState state, string message = null, bool canRetry = false)
        {
            HistoryLoadStatus = new ConversationLoadStatus<ConversationHistoryLoadState>(state, message, canRetry);
            HistoryLoadStatusChanged?.Invoke();
        }

        private void UpdateConversationSearchState()
        {
            if (_view == null)
            {
                return;
            }

            PopulateConversationPopup(_view.SearchText);
        }

        private void PopulateConversationPopup(string filter = null)
        {
            if (_view == null)
            {
                return;
            }

            _view.Render(new ConversationMenuViewState(
                isVisible: IsPopupOpen,
                searchText: filter ?? string.Empty,
                isSearchEnabled: ListLoadStatus.State != ConversationListLoadState.Loading
                    && ListLoadStatus.State != ConversationListLoadState.Initializing,
                entries: BuildConversationEntries(filter)));
        }

        private IReadOnlyList<ConversationMenuEntryViewState> BuildConversationEntries(string filter)
        {
            switch (ListLoadStatus.State)
            {
                case ConversationListLoadState.Initializing:
                    return new[]
                    {
                        new ConversationMenuEntryViewState(
                            ConversationMenuEntryKind.Status,
                            ListLoadStatus.Message ?? GetInitializationMessage(),
                            canRetry: false,
                            showSpinner: true)
                    };

                case ConversationListLoadState.Loading:
                    return new[]
                    {
                        new ConversationMenuEntryViewState(
                            ConversationMenuEntryKind.Status,
                            ListLoadStatus.Message ?? "Loading sessions...",
                            canRetry: false,
                            showSpinner: true)
                    };

                case ConversationListLoadState.Error:
                    return new[]
                    {
                        new ConversationMenuEntryViewState(
                            ConversationMenuEntryKind.Status,
                            ListLoadStatus.Message ?? "Failed to load sessions.",
                            canRetry: true)
                    };
            }

            var savedConversations = Conversations
                .Where(conversation => !string.IsNullOrEmpty(conversation.SessionId))
                .ToList();

            var filtered = string.IsNullOrEmpty(filter)
                ? savedConversations
                : savedConversations.Where(conversation => conversation.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            if (filtered.Count == 0)
            {
                return new[]
                {
                    new ConversationMenuEntryViewState(
                        ConversationMenuEntryKind.Status,
                        "No sessions found.")
                };
            }

            var today = DateTime.Today;
            var entries = new List<ConversationMenuEntryViewState>();
            foreach (var group in filtered.GroupBy(conversation => GetDateGroup(conversation.UpdatedAt, today)).OrderBy(group => group.Key))
            {
                entries.Add(new ConversationMenuEntryViewState(
                    ConversationMenuEntryKind.Header,
                    GetDateGroupLabel(group.Key)));

                foreach (var conversation in group.OrderByDescending(item => item.UpdatedAt))
                {
                    entries.Add(new ConversationMenuEntryViewState(
                        ConversationMenuEntryKind.Conversation,
                        conversation.Title,
                        GetRelativeTimeString(conversation.UpdatedAt),
                        isSelected: conversation == CurrentConversation,
                        conversation: conversation));
                }
            }

            return entries;
        }

        private async void RetryLoadFromPopup()
        {
            await LoadSessionListAsync(
                restoreSavedConversation: false,
                preserveCurrentConversation: true,
                reloadCurrentConversation: false);
        }

        private async void HandleConversationSelected(Conversation conversation)
        {
            await SelectConversationAsync(conversation);
            ClosePopup();
        }

        private string GetInitializationMessage()
        {
            return $"Initializing {_providerDisplayName} agent...";
        }

        private static int GetDateGroup(DateTime date, DateTime today)
        {
            if (date.Date == today) return 0;
            if (date.Date == today.AddDays(-1)) return 1;
            if (date >= today.AddDays(-7)) return 2;
            if (date >= today.AddDays(-30)) return 3;
            return 4;
        }

        private static string GetDateGroupLabel(int group)
        {
            return group switch
            {
                0 => "Today",
                1 => "Yesterday",
                2 => "This Week",
                3 => "This Month",
                _ => "Older"
            };
        }

        private static string GetRelativeTimeString(DateTime dateTime)
        {
            var diff = DateTime.Now - dateTime;

            if (diff.TotalMinutes < 1) return "now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d";

            return dateTime.ToString("MMM d");
        }
    }
}
