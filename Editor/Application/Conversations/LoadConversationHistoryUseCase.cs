// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEngine;

namespace Ryx.Sidekick.Editor.UseCases.Conversations
{
    internal sealed class LoadConversationHistoryRequest
    {
        public Conversation Conversation { get; set; }
        public bool ForceReload { get; set; }
    }

    internal sealed class LoadConversationHistoryResult
    {
        public Conversation Conversation { get; set; }
        public ConversationLoadStatus<ConversationHistoryLoadState> FinalHistoryStatus { get; set; }
        public ConversationUsagePayload Usage { get; set; }
    }

    internal sealed class LoadConversationHistoryUseCase
    {
        private readonly IConversationRepository _conversationRepository;
        private readonly IPersistentSessionBackend _sessionBackend;
        private readonly string _providerDisplayName;

        public LoadConversationHistoryUseCase(
            IConversationRepository conversationRepository,
            IPersistentSessionBackend sessionBackend,
            string providerDisplayName)
        {
            _conversationRepository = conversationRepository;
            _sessionBackend = sessionBackend;
            _providerDisplayName = string.IsNullOrWhiteSpace(providerDisplayName) ? "Provider" : providerDisplayName;
        }

        public async Task<LoadConversationHistoryResult> ExecuteAsync(
            LoadConversationHistoryRequest request,
            Action<ConversationLoadProgress> progress)
        {
            var conversation = request?.Conversation;
            if (conversation == null || string.IsNullOrEmpty(conversation.SessionId))
            {
                var emptyStatus = CreateHistoryStatus(ConversationHistoryLoadState.Empty);
                EmitProgress(progress, historyStatus: emptyStatus, usage: ConversationUsagePayload.None());
                return new LoadConversationHistoryResult
                {
                    Conversation = conversation,
                    FinalHistoryStatus = emptyStatus,
                    Usage = ConversationUsagePayload.None()
                };
            }

            var isInitializing = IsSessionBackendInitializingOrPending();
            if (isInitializing)
            {
                EmitProgress(
                    progress,
                    historyStatus: CreateHistoryStatus(
                        ConversationHistoryLoadState.Initializing,
                        GetInitializationMessage()));
            }

            var initializationException = await EnsureSessionBackendInitializedAsync();
            if (initializationException != null)
            {
                var initializationError = CreateHistoryStatus(
                    ConversationHistoryLoadState.Error,
                    BuildInitializationErrorMessage(initializationException),
                    canRetry: true);
                EmitProgress(progress, historyStatus: initializationError, usage: ConversationUsagePayload.None());
                return new LoadConversationHistoryResult
                {
                    Conversation = conversation,
                    FinalHistoryStatus = initializationError,
                    Usage = ConversationUsagePayload.None()
                };
            }

            EmitProgress(progress, historyStatus: CreateHistoryStatus(ConversationHistoryLoadState.Loading, "Loading conversation..."));

            Conversation fullConversation = null;
            try
            {
                var shouldLoadConversation = request?.ForceReload == true
                    || conversation.Messages == null
                    || conversation.Messages.Count == 0;
                if (shouldLoadConversation)
                {
                    fullConversation = await _conversationRepository.LoadConversationAsync(conversation.SessionId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ryx Sidekick] Failed to load conversation {conversation.SessionId}: {ex.Message}");
                var loadError = CreateHistoryStatus(
                    ConversationHistoryLoadState.Error,
                    "Failed to load conversation history.",
                    canRetry: true);
                EmitProgress(progress, historyStatus: loadError, usage: ConversationUsagePayload.None());
                return new LoadConversationHistoryResult
                {
                    Conversation = conversation,
                    FinalHistoryStatus = loadError,
                    Usage = ConversationUsagePayload.None()
                };
            }

            if (fullConversation != null)
            {
                MergeConversation(conversation, fullConversation);
            }

            ConversationUsageInfo usage = null;
            try
            {
                usage = await _conversationRepository.GetSessionUsageAsync(conversation.SessionId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ryx Sidekick] Failed to load conversation usage {conversation.SessionId}: {ex.Message}");
            }

            var finalStatus = conversation.Messages != null && conversation.Messages.Count > 0
                ? CreateHistoryStatus(ConversationHistoryLoadState.Ready)
                : CreateHistoryStatus(ConversationHistoryLoadState.Empty, "Conversation is empty.");
            var usagePayload = ConversationUsagePayload.FromUsage(usage);
            EmitProgress(progress, historyStatus: finalStatus, usage: usagePayload);
            return new LoadConversationHistoryResult
            {
                Conversation = conversation,
                FinalHistoryStatus = finalStatus,
                Usage = usagePayload
            };
        }

        private static void MergeConversation(Conversation target, Conversation source)
        {
            target.Messages = source.Messages ?? new List<Message>();
            if (!string.IsNullOrWhiteSpace(source.Title))
            {
                target.Title = source.Title;
            }

            target.CreatedAt = source.CreatedAt;
            target.UpdatedAt = source.UpdatedAt;
            if (!string.IsNullOrWhiteSpace(source.HistoryFilePath))
            {
                target.HistoryFilePath = source.HistoryFilePath;
            }
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
            ConversationLoadStatus<ConversationHistoryLoadState> historyStatus = null,
            ConversationUsagePayload usage = null)
        {
            progress?.Invoke(new ConversationLoadProgress
            {
                HistoryStatus = historyStatus,
                Usage = usage,
                ShouldRefreshUi = true
            });
        }

        private bool IsSessionBackendInitializingOrPending()
        {
            return _sessionBackend != null && _sessionBackend.BootstrapState != PersistentSessionBootstrapState.Ready;
        }

        private async Task<Exception> EnsureSessionBackendInitializedAsync()
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
                return ex;
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
