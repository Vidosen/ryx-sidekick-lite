// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Conversations
{
    internal sealed class SelectConversationRequest
    {
        public Conversation Conversation { get; set; }
    }

    internal sealed class SelectConversationResult
    {
        public Conversation SelectedConversation { get; set; }
        public ConversationLoadStatus<ConversationHistoryLoadState> FinalHistoryStatus { get; set; }
        public ConversationUsagePayload Usage { get; set; }
    }

    internal sealed class SelectConversationUseCase
    {
        private readonly ISettingsStore _settingsStore;
        private readonly LoadConversationHistoryUseCase _loadConversationHistoryUseCase;

        public SelectConversationUseCase(
            ISettingsStore settingsStore,
            LoadConversationHistoryUseCase loadConversationHistoryUseCase)
        {
            _settingsStore = settingsStore;
            _loadConversationHistoryUseCase = loadConversationHistoryUseCase
                ?? throw new ArgumentNullException(nameof(loadConversationHistoryUseCase));
        }

        public async Task<SelectConversationResult> ExecuteAsync(
            SelectConversationRequest request,
            Action<ConversationLoadProgress> progress)
        {
            var selectedConversation = request?.Conversation;
            if (selectedConversation == null)
            {
                var emptyStatus = new ConversationLoadStatus<ConversationHistoryLoadState>(ConversationHistoryLoadState.Empty);
                progress?.Invoke(new ConversationLoadProgress
                {
                    HistoryStatus = emptyStatus,
                    Usage = ConversationUsagePayload.None(),
                    ShouldRefreshUi = true
                });
                return new SelectConversationResult
                {
                    SelectedConversation = null,
                    FinalHistoryStatus = emptyStatus,
                    Usage = ConversationUsagePayload.None()
                };
            }

            if (string.IsNullOrEmpty(selectedConversation.SessionId))
            {
                var emptyStatus = new ConversationLoadStatus<ConversationHistoryLoadState>(ConversationHistoryLoadState.Empty);
                progress?.Invoke(new ConversationLoadProgress
                {
                    HistoryStatus = emptyStatus,
                    Usage = ConversationUsagePayload.None(),
                    ShouldRefreshUi = true
                });
                return new SelectConversationResult
                {
                    SelectedConversation = selectedConversation,
                    FinalHistoryStatus = emptyStatus,
                    Usage = ConversationUsagePayload.None()
                };
            }

            _settingsStore.LastOpenedSessionId = selectedConversation.SessionId;
            var historyResult = await _loadConversationHistoryUseCase.ExecuteAsync(
                new LoadConversationHistoryRequest
                {
                    Conversation = selectedConversation,
                    ForceReload = selectedConversation.Messages == null || selectedConversation.Messages.Count == 0
                },
                progress);

            return new SelectConversationResult
            {
                SelectedConversation = selectedConversation,
                FinalHistoryStatus = historyResult.FinalHistoryStatus,
                Usage = historyResult.Usage
            };
        }
    }
}
