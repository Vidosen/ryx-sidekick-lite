// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Questions;
using Ryx.Sidekick.Editor.UseCases.Chat;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor.Presentation.Controllers
{
    internal sealed class ChatController : IDisposable
    {
        private readonly IRuntimeOrchestrator _runtimeOrchestrator;
        private readonly ISettingsStore _settingsStore;
        private readonly IEditorDialogService _dialogService;
        private readonly IEditorScheduler _scheduler;
        private readonly ConversationController _conversationController;
        private readonly AuthController _authController;
        private readonly AttachmentController _attachmentController;
        private readonly PermissionController _permissionController;
        private readonly AssetRefreshController _assetRefreshController;
        private readonly SidekickStoreService _storeService;
        private readonly ChatSessionState _chatSessionState;
        private readonly SendPromptUseCase _sendPromptUseCase;
        private readonly StopTurnUseCase _stopTurnUseCase;
        private readonly TurnStreamAccumulator _turnStreamAccumulator;
        private readonly HandleRuntimeEventUseCase _handleRuntimeEventUseCase;

        private readonly IChatTimelineSink _timelineSink;

        private IComposerView _composerView;
        private ComposerViewModel _composerViewModel;

        private QueuedSendRequest _queuedSendRequest;

        // Context usage tracking

        public bool TurnActive { get; private set; }

        /// <summary>
        /// Returns true if a turn is in progress, checking the runtime orchestrator as authoritative source.
        /// Use this for UI decisions; TurnActive may lag behind due to event timing.
        /// </summary>
        public bool IsTurnInProgress => _runtimeOrchestrator?.IsTurnInProgress ?? TurnActive;
        public Message CurrentStreamingMessage => _turnStreamAccumulator.CurrentStreamingMessage;
        public bool IsThinkingActive => _turnStreamAccumulator.IsThinkingActive;

        public int LastUsedTokens { get; private set; }

        public int LastContextWindow { get; private set; }

        public IReadOnlyDictionary<string, ToolUse> ActiveTools => _turnStreamAccumulator.ActiveTools;
        
        /// <summary>
        /// Fired when context usage is updated (usedTokens, contextWindow).
        /// </summary>
        public event Action<int, int> OnContextUsageUpdated;
        
        /// <summary>
        /// Fired when TurnActive changes (true = turn started, false = turn ended).
        /// </summary>
        public event Action<bool> OnTurnActiveChanged;

        public ChatController(
            IRuntimeOrchestrator runtimeOrchestrator,
            ISettingsStore settingsStore,
            ConversationController conversationController,
            AuthController authController,
            AttachmentController attachmentController,
            PermissionController permissionController,
            AssetRefreshController assetRefreshController,
            IChatTimelineSink timelineSink,
            IEditorDialogService dialogService = null,
            IEditorScheduler scheduler = null,
            SidekickStoreService storeService = null,
            ChatSessionState chatSessionState = null,
            SendPromptUseCase sendPromptUseCase = null,
            StopTurnUseCase stopTurnUseCase = null,
            IClock clock = null,
            TurnStreamAccumulator turnStreamAccumulator = null,
            HandleRuntimeEventUseCase handleRuntimeEventUseCase = null)
        {
            _runtimeOrchestrator = runtimeOrchestrator;
            _settingsStore = settingsStore;
            _dialogService = dialogService ?? new UnityEditorDialogService();
            _scheduler = scheduler ?? new UnityEditorScheduler();
            _conversationController = conversationController;
            _authController = authController;
            _attachmentController = attachmentController;
            _permissionController = permissionController;
            _assetRefreshController = assetRefreshController;

            _timelineSink = timelineSink ?? throw new ArgumentNullException(nameof(timelineSink));
            _storeService = storeService;
            _chatSessionState = chatSessionState ?? new ChatSessionState(
                new ConversationControllerChatConversationSession(conversationController),
                settingsStore,
                () => _runtimeOrchestrator?.IsTurnInProgress ?? TurnActive);
            _turnStreamAccumulator = turnStreamAccumulator ?? new TurnStreamAccumulator();
            _sendPromptUseCase = sendPromptUseCase ?? new SendPromptUseCase(_runtimeOrchestrator, _chatSessionState);
            _stopTurnUseCase = stopTurnUseCase ?? new StopTurnUseCase(_runtimeOrchestrator, _chatSessionState);
            _handleRuntimeEventUseCase = handleRuntimeEventUseCase ?? new HandleRuntimeEventUseCase(
                _chatSessionState,
                _turnStreamAccumulator,
                clock ?? new SystemClock(),
                _settingsStore);
        }

        public void BindView(IComposerView composerView)
        {
            _composerView = composerView;
        }

        public void BindComposerViewModel(ComposerViewModel composerViewModel)
        {
            if (_composerViewModel != null)
            {
                _composerViewModel.SendRequested -= HandleComposerSendRequested;
                _composerViewModel.StopRequested -= HandleComposerStopRequested;
                _composerViewModel.CompactRequested -= HandleComposerCompactRequested;
            }

            _composerViewModel = composerViewModel;

            if (_composerViewModel != null)
            {
                _composerViewModel.SendRequested += HandleComposerSendRequested;
                _composerViewModel.StopRequested += HandleComposerStopRequested;
                _composerViewModel.CompactRequested += HandleComposerCompactRequested;
            }
        }

        private void HandleComposerSendRequested(ComposerSendIntent intent)
        {
            SendMessage(intent.Text, intent.Attachments, intent.ContextAttachments);
        }

        private void HandleComposerStopRequested(ComposerSendIntent queuedIntent)
        {
            if (queuedIntent != null &&
                (!string.IsNullOrWhiteSpace(queuedIntent.Text)
                 || (queuedIntent.Attachments != null && queuedIntent.Attachments.Count > 0)
                 || (queuedIntent.ContextAttachments != null && queuedIntent.ContextAttachments.Count > 0)))
            {
                _queuedSendRequest = new QueuedSendRequest(
                    queuedIntent.Text,
                    queuedIntent.Attachments,
                    queuedIntent.ContextAttachments);
            }

            StopRequest();
            TryDispatchQueuedSend();
        }

        private void HandleComposerCompactRequested()
        {
            SendMessage("/compact", null, null);
        }

        public void Dispose()
        {
            if (_composerViewModel != null)
            {
                _composerViewModel.SendRequested -= HandleComposerSendRequested;
                _composerViewModel.StopRequested -= HandleComposerStopRequested;
                _composerViewModel.CompactRequested -= HandleComposerCompactRequested;
                _composerViewModel = null;
            }

            UnsubscribeFromProcessEvents();
        }

        public bool TryGetActiveTool(string toolUseId, out ToolUse toolUse)
        {
            return _turnStreamAccumulator.ActiveTools.TryGetValue(toolUseId, out toolUse);
        }

        public void ApplyAskUserQuestionTraceAnswers(string toolUseId, JObject answersPayload)
        {
            if (string.IsNullOrEmpty(toolUseId) || answersPayload == null)
            {
                return;
            }

            if (!_turnStreamAccumulator.ActiveTools.TryGetValue(toolUseId, out var tool) || tool == null)
            {
                return;
            }

            tool.Input = AskUserQuestionTraceFormatter.ApplyAnswers(tool.Input, answersPayload);
            _timelineSink.UpdateToolMessage(toolUseId);
        }

        public void SubscribeToProcessEvents()
        {
            if (_runtimeOrchestrator == null) return;

            _runtimeOrchestrator.OnTextDelta += HandleTextDelta;
            _runtimeOrchestrator.OnAssistantMessageStarted += HandleAssistantMessageStarted;
            _runtimeOrchestrator.OnToolUse += HandleToolUse;
            _runtimeOrchestrator.OnToolResult += HandleToolResult;
            if (_permissionController != null)
            {
                _runtimeOrchestrator.OnPermissionRequest += _permissionController.HandlePermissionRequest;
            }
            _runtimeOrchestrator.OnError += HandleError;
            _runtimeOrchestrator.OnStreamComplete += HandleStreamComplete;
            _runtimeOrchestrator.OnRawOutput += HandleRawOutput;
            _runtimeOrchestrator.OnImageAttachment += HandleImageAttachment;
            _runtimeOrchestrator.OnTurnStarted += HandleTurnStarted;
            _runtimeOrchestrator.OnTurnFinished += HandleTurnFinished;

            // Thinking events
            _runtimeOrchestrator.OnThinkingStarted += HandleThinkingStarted;
            _runtimeOrchestrator.OnThinkingDelta += HandleThinkingDelta;
            _runtimeOrchestrator.OnThinkingCompleted += HandleThinkingCompleted;
            
            // Context usage events
            _runtimeOrchestrator.OnContextUsageUpdated += HandleContextUsageUpdated;
        }

        public void UnsubscribeFromProcessEvents()
        {
            if (_runtimeOrchestrator == null) return;

            _runtimeOrchestrator.OnTextDelta -= HandleTextDelta;
            _runtimeOrchestrator.OnAssistantMessageStarted -= HandleAssistantMessageStarted;
            _runtimeOrchestrator.OnToolUse -= HandleToolUse;
            _runtimeOrchestrator.OnToolResult -= HandleToolResult;
            if (_permissionController != null)
            {
                _runtimeOrchestrator.OnPermissionRequest -= _permissionController.HandlePermissionRequest;
            }
            _runtimeOrchestrator.OnError -= HandleError;
            _runtimeOrchestrator.OnStreamComplete -= HandleStreamComplete;
            _runtimeOrchestrator.OnRawOutput -= HandleRawOutput;
            _runtimeOrchestrator.OnImageAttachment -= HandleImageAttachment;
            _runtimeOrchestrator.OnTurnStarted -= HandleTurnStarted;
            _runtimeOrchestrator.OnTurnFinished -= HandleTurnFinished;

            // Thinking events
            _runtimeOrchestrator.OnThinkingStarted -= HandleThinkingStarted;
            _runtimeOrchestrator.OnThinkingDelta -= HandleThinkingDelta;
            _runtimeOrchestrator.OnThinkingCompleted -= HandleThinkingCompleted;
            
            // Context usage events
            _runtimeOrchestrator.OnContextUsageUpdated -= HandleContextUsageUpdated;
        }

        public async void SendMessage(
            string text,
            IReadOnlyList<ImageAttachment> attachments,
            IReadOnlyList<IContextAttachment> contextAttachments = null)
        {
            try
            {
                var previousConversation = _conversationController?.CurrentConversation;
                var previousMessageCount = previousConversation?.Messages?.Count ?? 0;

                var sendTask = _sendPromptUseCase.ExecuteAsync(new SendPromptRequest
                {
                    Text = text,
                    Attachments = attachments,
                    ContextAttachments = contextAttachments,
                    RequiresAuthentication = _authController?.RequiresAuth() == true
                });

                var uiUpdatedForPreparedState = RefreshUiIfPreparedStateChanged(previousConversation, previousMessageCount);
                var result = await sendTask;

                HandleSendPromptResult(result, uiUpdatedForPreparedState);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public void SendMessageFromUi()
        {
            var request = CreateQueuedSendRequestFromUi();
            SendMessage(request.Text, request.Attachments, request.ContextAttachments);
        }

        public void HandleEnterKeyFromUi()
        {
            var request = CreateQueuedSendRequestFromUi();

            if (IsTurnInProgress)
            {
                if (request.HasContent)
                {
                    _queuedSendRequest = request;
                    StopRequest();
                    TryDispatchQueuedSend();
                    return;
                }

                StopRequest();
                return;
            }

            SendMessage(request.Text, request.Attachments, request.ContextAttachments);
        }

        public void SendLocalFollowupMessage(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (IsTurnInProgress)
            {
                _scheduler.Schedule(() => SendLocalFollowupMessage(text));
                return;
            }

            SendMessage(text, null, null);
        }

        public void StopRequest()
        {
            var result = _stopTurnUseCase.Execute(new StopTurnRequest
            {
                CurrentStreamingMessage = CurrentStreamingMessage
            });

            if (result.Status != StopTurnStatus.Stopped)
            {
                return;
            }

            _storeService?.SetTurnStopRequested();
            if (result.ShouldClearCurrentStreamingMessage)
            {
                _turnStreamAccumulator.CurrentStreamingMessage = null;
            }

            _timelineSink.Refresh();
        }

        private void HandleSendPromptResult(SendPromptResult result, bool uiUpdatedForPreparedState)
        {
            switch (result.Status)
            {
                case SendPromptStatus.RequiresAuthentication:
                {
                    var shouldLogin = _dialogService.DisplayDialog(
                        "Authentication Required",
                        "You need to log in before sending messages to Claude.",
                        "Login",
                        "Cancel");

                    if (shouldLogin)
                    {
                        _authController?.ShowLoginOptions();
                    }

                    break;
                }
                case SendPromptStatus.ConversationLoading:
                    _dialogService.DisplayDialog(
                        "Conversation Loading",
                        "Wait for the selected conversation to finish loading before sending a new message.",
                        "OK");
                    break;
                case SendPromptStatus.Started:
                    if (result.ShouldClearComposer)
                    {
                        _composerView?.BlurPrompt();
                        if (_composerView != null)
                        {
                            _composerView.PromptText = string.Empty;
                        }

                        _composerView?.FocusPrompt();
                        _attachmentController?.ClearPendingAttachments(destroyTextures: false);
                    }

                    _timelineSink.Refresh();
                    break;
                case SendPromptStatus.TurnAlreadyInProgress:
                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        Debug.LogWarning($"[Ryx Sidekick] {result.ErrorMessage}");
                    }
                    break;
                case SendPromptStatus.RuntimeStartFailed:
                    if (!uiUpdatedForPreparedState && result.UserMessageAdded)
                    {
                        _timelineSink.Refresh();
                    }

                    if (!string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        Debug.LogWarning($"[Ryx Sidekick] {result.ErrorMessage}");
                    }

                    break;
            }
        }


        private void HandleTurnStarted()
        {
            ApplyRuntimeEvent(RuntimeEventKind.TurnStarted);
        }

        private void HandleTurnFinished()
        {
            ApplyRuntimeEvent(RuntimeEventKind.TurnFinished);
        }

        private void HandleTextDelta(string delta)
        {
            ApplyRuntimeEvent(RuntimeEventKind.TextDelta, text: delta);
        }

        private void HandleAssistantMessageStarted()
        {
            ApplyRuntimeEvent(RuntimeEventKind.AssistantMessageStarted);
        }

        private void HandleImageAttachment(ImageAttachment attachment)
        {
            ApplyRuntimeEvent(RuntimeEventKind.ImageAttachmentReceived, imageAttachment: attachment);
        }

        internal void HandleToolUse(ToolUse toolUse)
        {
            ApplyRuntimeEvent(RuntimeEventKind.ToolUseReceived, toolUse: toolUse);
        }

        private void HandleToolResult(string toolUseId, string content)
        {
            ApplyRuntimeEvent(RuntimeEventKind.ToolResultReceived, toolUseId: toolUseId, toolResultContent: content);
        }

        private void HandleError(string error)
        {
            Debug.LogError($"[Ryx Sidekick] {error}");
            ReportSystemError(error);
        }

        private void HandleStreamComplete()
        {
            ApplyRuntimeEvent(RuntimeEventKind.StreamCompleted, sessionId: _runtimeOrchestrator?.CurrentSessionId);
        }

        private void HandleRawOutput(string line)
        {
            if (_settingsStore == null || (!_settingsStore.DebugMode && !_settingsStore.VerboseLogging)) return;
            if (string.IsNullOrWhiteSpace(line)) return;
            Debug.Log($"[Ryx Sidekick][CLI] {line}");
        }

        public void ReportSystemError(string error)
        {
            ApplyRuntimeEvent(RuntimeEventKind.ErrorOccurred, text: error);
        }

        private void HandleThinkingStarted()
        {
            ApplyRuntimeEvent(RuntimeEventKind.ThinkingStarted);
        }

        private void HandleThinkingDelta(string chunk)
        {
            ApplyRuntimeEvent(RuntimeEventKind.ThinkingDelta, text: chunk);
        }

        private void HandleThinkingCompleted(string fullText)
        {
            ApplyRuntimeEvent(RuntimeEventKind.ThinkingCompleted, text: fullText);
        }

        private void HandleContextUsageUpdated(int usedTokens, int contextWindow)
        {
            ApplyRuntimeEvent(RuntimeEventKind.ContextUsageUpdated, usedTokens: usedTokens, contextWindow: contextWindow);
        }

        private void ApplyRuntimeEvent(
            RuntimeEventKind kind,
            string text = null,
            ToolUse toolUse = null,
            string toolUseId = null,
            string toolResultContent = null,
            ImageAttachment imageAttachment = null,
            int usedTokens = 0,
            int contextWindow = 0,
            string sessionId = null)
        {
            var result = _handleRuntimeEventUseCase.Handle(new HandleRuntimeEventRequest
            {
                Kind = kind,
                Text = text,
                ToolUse = toolUse,
                ToolUseId = toolUseId,
                ToolResultContent = toolResultContent,
                ImageAttachment = imageAttachment,
                UsedTokens = usedTokens,
                ContextWindow = contextWindow,
                SessionId = sessionId
            });

            ApplyRuntimeEventResult(result);
        }

        private void ApplyRuntimeEventResult(HandleRuntimeEventResult result)
        {
            if (result == null)
            {
                return;
            }

            foreach (var message in result.AddedMessages)
            {
                if (message == null)
                {
                    continue;
                }

                if (message.IsThinkingBlock)
                {
                    _timelineSink.AppendThinkingMessage(message);
                    continue;
                }

                if (message.Role == MessageRole.Tool)
                {
                    _timelineSink.AppendToolMessage(message);
                    continue;
                }

                _timelineSink.AppendMessage(message);
            }

            var conversation = _chatSessionState?.CurrentConversation;
            var updatedStreamingMessage = false;
            foreach (var messageId in result.UpdatedMessageIds)
            {
                var message = conversation?.Messages?.FirstOrDefault(candidate => candidate.Id == messageId);
                if (message?.IsThinkingBlock == true)
                {
                    _timelineSink.UpdateThinkingMessage(messageId);
                    continue;
                }

                if (!updatedStreamingMessage)
                {
                    _timelineSink.UpdateStreamingMessage();
                    updatedStreamingMessage = true;
                }
            }

            foreach (var toolUseId in result.UpdatedToolUseIds)
            {
                _timelineSink.UpdateToolMessage(toolUseId);
            }

            if (result.ContextUsage != null)
            {
                LastUsedTokens = result.ContextUsage.UsedTokens;
                LastContextWindow = result.ContextUsage.ContextWindow;
                _storeService?.SetContextUsage(result.ContextUsage.UsedTokens, result.ContextUsage.ContextWindow);
                OnContextUsageUpdated?.Invoke(result.ContextUsage.UsedTokens, result.ContextUsage.ContextWindow);
            }

            if (result.TurnActiveChanged.HasValue)
            {
                TurnActive = result.TurnActiveChanged.Value;

                if (TurnActive)
                {
                    _storeService?.SetTurnStarted();
                }
                else
                {
                    _storeService?.SetTurnFinished();
                }

                OnTurnActiveChanged?.Invoke(TurnActive);
            }

            if (result.ShouldResetPendingPermissions)
            {
                _permissionController?.ResetPending();
            }

            if (result.ToolUseForAssetRefresh != null)
            {
                _assetRefreshController?.OnToolUse(result.ToolUseForAssetRefresh);
            }

            if (result.ToolResultForAssetRefresh != null)
            {
                _assetRefreshController?.OnToolResult(result.ToolResultForAssetRefresh);
            }

            if (result.ShouldNotifyAssetRefreshOnStreamComplete)
            {
                _assetRefreshController?.OnStreamComplete();
            }

            if (result.ShouldRefreshUi)
            {
                _timelineSink.Refresh();
            }

            if (result.TurnActiveChanged == false)
            {
                TryDispatchQueuedSend();
            }
        }

        /// <summary>
        /// Agent Host reconnect (Phase 3): selects the conversation for <paramref name="sessionId"/> so
        /// the daemon's replayed in-flight turn lands in the right timeline, WITHOUT sending the
        /// synthetic "Continue where you left off" prompt and WITHOUT the <c>&lt;domain_reload/&gt;</c>
        /// banner. The live runtime was already re-attached by the host; this only restores the UI
        /// selection. Use cases:
        /// <list type="bullet">
        /// <item>successful re-attach → this method (no prompt, the surviving turn streams on)</item>
        /// <item>failed re-attach → <see cref="AutoResumeAfterDomainReload"/> (synthetic resume prompt)</item>
        /// </list>
        /// </summary>
        public async System.Threading.Tasks.Task RestoreConversationForReattachAsync(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return;
                }

                await EnsureConversationSelectedAsync(sessionId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Ensures a conversation for <paramref name="sessionId"/> exists, is current, and is loaded.
        /// Shared by the synthetic-resume path and the Agent Host reattach path.
        /// </summary>
        private async System.Threading.Tasks.Task EnsureConversationSelectedAsync(string sessionId)
        {
            var conv = _conversationController?.Conversations?.FirstOrDefault(c => c.SessionId == sessionId);
            if (conv == null && _conversationController != null)
            {
                conv = new Conversation
                {
                    Id = sessionId,
                    SessionId = sessionId,
                    Title = "Resumed Session"
                };
                _conversationController.Conversations.Insert(0, conv);
            }

            if (_conversationController != null)
            {
                _conversationController.SetCurrentConversation(conv);
                // ConversationController already lazy-loads; force here by selecting.
                await _conversationController.SelectConversationAsync(conv);
            }
        }

        public async void AutoResumeAfterDomainReload(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    Debug.LogWarning("[Ryx Sidekick] AutoResumeAfterDomainReload called with null/empty sessionId");
                    return;
                }

                try
                {
                    // Ensure we have a conversation for this session (current + loaded).
                    await EnsureConversationSelectedAsync(sessionId);
                    var conv = _conversationController?.CurrentConversation;

                    const string displayTag = "<domain_reload/>";
                    const string cliPrompt = "Unity recompilation and domain reload has completed. Continue where you left off.";

                    // Add display message to conversation (for UI rendering as banner)
                    if (conv != null)
                    {
                        var userMessage = new Message
                        {
                            Role = MessageRole.User,
                            Content = displayTag
                        };
                        conv.Messages.Add(userMessage);
                        conv.UpdatedAt = DateTime.Now;
                    }

                    _timelineSink.Refresh();

                    Debug.Log($"[Ryx Sidekick] Auto-resuming session {sessionId} after domain reload");

                    // Send clean prompt to CLI (not the display tag)
                    var dispatchResult = await _runtimeOrchestrator.SendPromptAsync(cliPrompt, sessionId: sessionId);
                    if (!dispatchResult.IsStarted)
                    {
                        ReportSystemError(dispatchResult.ErrorMessage ?? "Failed to resume session after domain reload.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                //ignore
            }
        }

        private QueuedSendRequest CreateQueuedSendRequestFromUi()
        {
            var text = _composerView?.PromptText ?? string.Empty;

            var attachments = _attachmentController?.PendingAttachments != null &&
                              _attachmentController.PendingAttachments.Count > 0
                ? _attachmentController.PendingAttachments.Where(a => a != null).ToList()
                : null;

            var contextAttachments = _attachmentController?.PendingContextAttachments != null &&
                                     _attachmentController.PendingContextAttachments.Count > 0
                ? _attachmentController.PendingContextAttachments.Where(a => a != null).ToList()
                : null;

            return new QueuedSendRequest(text, attachments, contextAttachments);
        }

        private bool RefreshUiIfPreparedStateChanged(Conversation previousConversation, int previousMessageCount)
        {
            var currentConversation = _conversationController?.CurrentConversation;
            if (!ReferenceEquals(previousConversation, currentConversation))
            {
                _timelineSink.Refresh();
                return true;
            }

            var currentMessageCount = currentConversation?.Messages?.Count ?? 0;
            if (currentMessageCount != previousMessageCount)
            {
                _timelineSink.Refresh();
                return true;
            }

            return false;
        }

        private void TryDispatchQueuedSend()
        {
            if (_queuedSendRequest == null || IsTurnInProgress)
            {
                return;
            }

            var queuedSend = _queuedSendRequest;
            _queuedSendRequest = null;
            _scheduler.Schedule(() => SendMessage(queuedSend.Text, queuedSend.Attachments, queuedSend.ContextAttachments));
        }

        private sealed class QueuedSendRequest
        {
            public QueuedSendRequest(
                string text,
                IReadOnlyList<ImageAttachment> attachments,
                IReadOnlyList<IContextAttachment> contextAttachments)
            {
                Text = text;
                Attachments = attachments?.ToList();
                ContextAttachments = contextAttachments?.ToList();
            }

            public string Text { get; }

            public IReadOnlyList<ImageAttachment> Attachments { get; }

            public IReadOnlyList<IContextAttachment> ContextAttachments { get; }

            public bool HasContent =>
                !string.IsNullOrWhiteSpace(Text)
                || (Attachments != null && Attachments.Count > 0)
                || (ContextAttachments != null && ContextAttachments.Count > 0);
        }
    }
}
