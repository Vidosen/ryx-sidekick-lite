// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEngine;

namespace Ryx.Sidekick.Editor.UseCases.Conversations
{
    internal sealed class LoadConversationListRequest
    {
        public bool RestoreSavedConversation { get; set; }
        public bool PreserveCurrentConversation { get; set; }
        public bool ReloadCurrentConversation { get; set; }
        public Conversation CurrentConversation { get; set; }
        public ConversationLoadStatus<ConversationHistoryLoadState> CurrentHistoryStatus { get; set; }
        public string LastOpenedSessionId { get; set; }
    }

    internal sealed class LoadConversationListResult
    {
        public List<Conversation> Conversations { get; set; }
        public Conversation SelectedConversation { get; set; }
        public ConversationLoadStatus<ConversationListLoadState> FinalListStatus { get; set; }
        public ConversationLoadStatus<ConversationHistoryLoadState> FinalHistoryStatus { get; set; }
        public ConversationLoadNextAction NextAction { get; set; }
        public ConversationUsagePayload Usage { get; set; }
    }

    internal sealed class LoadConversationListQuery
    {
        private readonly IConversationRepository _conversationRepository;
        private readonly IPersistentSessionBackend _sessionBackend;
        private readonly IClock _clock;
        private readonly string _providerDisplayName;

        public LoadConversationListQuery(
            IConversationRepository conversationRepository,
            IPersistentSessionBackend sessionBackend,
            IClock clock,
            string providerDisplayName)
        {
            _conversationRepository = conversationRepository;
            _sessionBackend = sessionBackend;
            _clock = clock;
            _providerDisplayName = string.IsNullOrWhiteSpace(providerDisplayName) ? "Provider" : providerDisplayName;
        }

        public async Task<LoadConversationListResult> ExecuteAsync(
            LoadConversationListRequest request,
            Action<ConversationLoadProgress> progress)
        {
            var currentConversation = request?.CurrentConversation;
            var currentSessionId = request?.PreserveCurrentConversation == true
                ? currentConversation?.SessionId
                : null;
            var keepUnsavedCurrent = request?.PreserveCurrentConversation == true
                && currentConversation != null
                && string.IsNullOrEmpty(currentConversation.SessionId);

            var conversations = keepUnsavedCurrent
                ? new List<Conversation> { currentConversation }
                : new List<Conversation>();

            var isInitializing = IsSessionBackendInitializingOrPending();
            EmitProgress(
                progress,
                listStatus: CreateListStatus(
                    isInitializing ? ConversationListLoadState.Initializing : ConversationListLoadState.Loading,
                    isInitializing ? GetInitializationMessage() : "Loading sessions..."),
                historyStatus: keepUnsavedCurrent
                    ? null
                    : CreateHistoryStatus(
                        isInitializing ? ConversationHistoryLoadState.Initializing : ConversationHistoryLoadState.Loading,
                        isInitializing ? GetInitializationMessage() : "Loading conversation..."));

            var initError = await EnsureSessionBackendInitializedAsync();
            if (!string.IsNullOrEmpty(initError))
            {
                var listErrorStatus = CreateListStatus(ConversationListLoadState.Error, initError, canRetry: true);
                var historyErrorStatus = keepUnsavedCurrent
                    ? request?.CurrentHistoryStatus
                    : CreateHistoryStatus(ConversationHistoryLoadState.Error, initError, canRetry: true);
                var usage = keepUnsavedCurrent ? null : ConversationUsagePayload.None();
                EmitProgress(progress, listStatus: listErrorStatus, historyStatus: historyErrorStatus, usage: usage);
                return new LoadConversationListResult
                {
                    Conversations = conversations,
                    FinalListStatus = listErrorStatus,
                    FinalHistoryStatus = historyErrorStatus,
                    NextAction = keepUnsavedCurrent ? ConversationLoadNextAction.None : ConversationLoadNextAction.EnsureEmptyConversation,
                    Usage = usage
                };
            }

            EmitProgress(
                progress,
                listStatus: CreateListStatus(ConversationListLoadState.Loading, "Loading sessions..."),
                historyStatus: keepUnsavedCurrent
                    ? null
                    : CreateHistoryStatus(ConversationHistoryLoadState.Loading, "Loading conversation..."));

            List<CliSessionInfo> sessions;
            try
            {
                sessions = await _conversationRepository.ListSessionsAsync() ?? new List<CliSessionInfo>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ryx Sidekick] Failed to load CLI sessions: {ex.Message}");
                var listErrorStatus = CreateListStatus(ConversationListLoadState.Error, "Failed to load sessions.", canRetry: true);
                var historyStatus = keepUnsavedCurrent
                    ? request?.CurrentHistoryStatus
                    : CreateHistoryStatus(ConversationHistoryLoadState.Empty);
                var usage = keepUnsavedCurrent ? null : ConversationUsagePayload.None();
                EmitProgress(progress, listStatus: listErrorStatus, historyStatus: historyStatus, usage: usage);
                return new LoadConversationListResult
                {
                    Conversations = conversations,
                    FinalListStatus = listErrorStatus,
                    FinalHistoryStatus = historyStatus,
                    NextAction = keepUnsavedCurrent ? ConversationLoadNextAction.None : ConversationLoadNextAction.EnsureEmptyConversation,
                    Usage = usage
                };
            }

            var restoredCurrent = RebuildConversationList(
                sessions,
                currentSessionId,
                currentConversation,
                keepUnsavedCurrent,
                conversations);

            var savedSessionCount = conversations.Count(c => !string.IsNullOrEmpty(c.SessionId));
            var finalListStatus = CreateListStatus(
                savedSessionCount > 0 ? ConversationListLoadState.Ready : ConversationListLoadState.Empty,
                savedSessionCount > 0 ? null : "No sessions found");

            Conversation selectedConversation = null;
            ConversationLoadNextAction nextAction = ConversationLoadNextAction.None;
            ConversationLoadStatus<ConversationHistoryLoadState> finalHistoryStatus = request?.CurrentHistoryStatus;
            ConversationUsagePayload usagePayload = null;

            if (request?.RestoreSavedConversation == true)
            {
                selectedConversation = TryRestoreSavedConversation(
                    conversations,
                    request.LastOpenedSessionId,
                    _clock?.Now ?? DateTime.Now);

                if (selectedConversation != null)
                {
                    nextAction = ConversationLoadNextAction.LoadSelectedHistory;
                    finalHistoryStatus = CreateHistoryStatus(ConversationHistoryLoadState.Loading, "Loading conversation...");
                    usagePayload = null;
                }
                else
                {
                    nextAction = ConversationLoadNextAction.EnsureEmptyConversation;
                    finalHistoryStatus = CreateHistoryStatus(ConversationHistoryLoadState.Empty);
                    usagePayload = ConversationUsagePayload.None();
                }
            }
            else if (!keepUnsavedCurrent)
            {
                selectedConversation = restoredCurrent;
                if (selectedConversation == null)
                {
                    nextAction = ConversationLoadNextAction.EnsureEmptyConversation;
                    finalHistoryStatus = CreateHistoryStatus(ConversationHistoryLoadState.Empty);
                    usagePayload = ConversationUsagePayload.None();
                }
                else if (request?.ReloadCurrentConversation == true && !string.IsNullOrEmpty(selectedConversation.SessionId))
                {
                    nextAction = ConversationLoadNextAction.LoadSelectedHistory;
                    finalHistoryStatus = CreateHistoryStatus(ConversationHistoryLoadState.Loading, "Loading conversation...");
                    usagePayload = null;
                }
                else
                {
                    finalHistoryStatus = CreateHistoryStatus(ConversationHistoryLoadState.Empty);
                    usagePayload = ConversationUsagePayload.None();
                }
            }
            else
            {
                selectedConversation = currentConversation;
            }

            EmitProgress(progress, listStatus: finalListStatus, historyStatus: finalHistoryStatus, usage: usagePayload);
            return new LoadConversationListResult
            {
                Conversations = conversations,
                SelectedConversation = selectedConversation,
                FinalListStatus = finalListStatus,
                FinalHistoryStatus = finalHistoryStatus,
                NextAction = nextAction,
                Usage = usagePayload
            };
        }

        private static Conversation RebuildConversationList(
            List<CliSessionInfo> sessions,
            string currentSessionId,
            Conversation currentConversation,
            bool keepUnsavedCurrent,
            List<Conversation> conversations)
        {
            var uniqueSessions = (sessions ?? new List<CliSessionInfo>())
                .Where(session => !string.IsNullOrEmpty(session.SessionId))
                .GroupBy(session => session.SessionId)
                .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
                .OrderByDescending(session => session.UpdatedAt)
                .ToList();

            Conversation preservedCurrent = null;
            foreach (var session in uniqueSessions)
            {
                if (!string.IsNullOrEmpty(currentSessionId)
                    && session.SessionId == currentSessionId
                    && currentConversation != null)
                {
                    currentConversation.HistoryFilePath = session.FilePath;
                    currentConversation.Title = session.Title;
                    currentConversation.CreatedAt = session.CreatedAt;
                    currentConversation.UpdatedAt = session.UpdatedAt;
                    preservedCurrent = currentConversation;
                    conversations.Add(currentConversation);
                    continue;
                }

                conversations.Add(new Conversation
                {
                    Id = session.SessionId,
                    SessionId = session.SessionId,
                    Title = session.Title,
                    CreatedAt = session.CreatedAt,
                    UpdatedAt = session.UpdatedAt,
                    HistoryFilePath = session.FilePath
                });
            }

            if (preservedCurrent != null)
            {
                return preservedCurrent;
            }

            if (!string.IsNullOrEmpty(currentSessionId))
            {
                return conversations.FirstOrDefault(conversation => conversation.SessionId == currentSessionId);
            }

            return keepUnsavedCurrent ? currentConversation : null;
        }

        private static Conversation TryRestoreSavedConversation(
            List<Conversation> conversations,
            string lastOpenedSessionId,
            DateTime now)
        {
            if (string.IsNullOrEmpty(lastOpenedSessionId))
            {
                return null;
            }

            var restoredConversation = conversations?.FirstOrDefault(conversation => conversation.SessionId == lastOpenedSessionId);
            if (restoredConversation == null)
            {
                return null;
            }

            var staleDuration = TimeSpan.FromHours(24);
            var isStale = (now - restoredConversation.UpdatedAt) > staleDuration;
            return isStale ? null : restoredConversation;
        }

        private static ConversationLoadStatus<ConversationListLoadState> CreateListStatus(
            ConversationListLoadState state,
            string message = null,
            bool canRetry = false)
        {
            return new ConversationLoadStatus<ConversationListLoadState>(state, message, canRetry);
        }

        private static ConversationLoadStatus<ConversationHistoryLoadState> CreateHistoryStatus(
            ConversationHistoryLoadState state,
            string message = null,
            bool canRetry = false)
        {
            return new ConversationLoadStatus<ConversationHistoryLoadState>(state, message, canRetry);
        }

        private static void EmitProgress(
            Action<ConversationLoadProgress> progress,
            ConversationLoadStatus<ConversationListLoadState> listStatus = null,
            ConversationLoadStatus<ConversationHistoryLoadState> historyStatus = null,
            ConversationUsagePayload usage = null)
        {
            progress?.Invoke(new ConversationLoadProgress
            {
                ListStatus = listStatus,
                HistoryStatus = historyStatus,
                Usage = usage,
                ShouldRefreshUi = true
            });
        }

        private bool IsSessionBackendInitializingOrPending()
        {
            return _sessionBackend != null && _sessionBackend.BootstrapState != PersistentSessionBootstrapState.Ready;
        }

        private async Task<string> EnsureSessionBackendInitializedAsync()
        {
            if (_sessionBackend == null || _sessionBackend.BootstrapState == PersistentSessionBootstrapState.Ready)
            {
                return null;
            }

            try
            {
                await _sessionBackend.EnsureInitializedAsync();
                return null;
            }
            catch (Exception ex)
            {
                return BuildInitializationErrorMessage(ex);
            }
        }

        private string GetInitializationMessage()
        {
            return $"Initializing {_providerDisplayName} agent...";
        }

        private string BuildInitializationErrorMessage(Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(_sessionBackend?.BootstrapErrorMessage))
            {
                return _sessionBackend.BootstrapErrorMessage;
            }

            if (!string.IsNullOrWhiteSpace(ex?.Message))
            {
                return $"Failed to initialize {_providerDisplayName} agent: {ex.Message}";
            }

            return $"Failed to initialize {_providerDisplayName} agent.";
        }
    }
}
