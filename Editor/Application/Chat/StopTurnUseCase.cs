// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Chat
{
    internal enum StopTurnStatus
    {
        IgnoredNoActiveTurn,
        Stopped
    }

    internal sealed class StopTurnRequest
    {
        public Message CurrentStreamingMessage { get; set; }
    }

    internal sealed class StopTurnResult
    {
        public StopTurnResult(
            StopTurnStatus status,
            bool sessionIdCaptured = false,
            bool streamingMessageUpdated = false,
            bool shouldClearCurrentStreamingMessage = false)
        {
            Status = status;
            SessionIdCaptured = sessionIdCaptured;
            StreamingMessageUpdated = streamingMessageUpdated;
            ShouldClearCurrentStreamingMessage = shouldClearCurrentStreamingMessage;
        }

        public StopTurnStatus Status { get; }
        public bool SessionIdCaptured { get; }
        public bool StreamingMessageUpdated { get; }
        public bool ShouldClearCurrentStreamingMessage { get; }
    }

    internal sealed class StopTurnUseCase
    {
        private readonly IRuntimeOrchestrator _runtimeOrchestrator;
        private readonly ChatSessionState _chatSessionState;

        public StopTurnUseCase(
            IRuntimeOrchestrator runtimeOrchestrator,
            ChatSessionState chatSessionState)
        {
            _runtimeOrchestrator = runtimeOrchestrator;
            _chatSessionState = chatSessionState;
        }

        public StopTurnResult Execute(StopTurnRequest request)
        {
            var hasRuntimeState = _chatSessionState?.IsTurnInProgress == true
                || request?.CurrentStreamingMessage != null
                || !string.IsNullOrEmpty(_runtimeOrchestrator?.CurrentSessionId);

            if (_runtimeOrchestrator == null || !hasRuntimeState)
            {
                return new StopTurnResult(StopTurnStatus.IgnoredNoActiveTurn);
            }

            _runtimeOrchestrator.Stop();

            var streamingMessageUpdated = false;
            if (request?.CurrentStreamingMessage != null)
            {
                request.CurrentStreamingMessage.Content += "\n\n[Stopped by user]";
                request.CurrentStreamingMessage.IsStreaming = false;
                streamingMessageUpdated = true;
            }

            var sessionIdCaptured = _chatSessionState?.TryCaptureRuntimeSessionId(_runtimeOrchestrator.CurrentSessionId) == true;
            return new StopTurnResult(
                StopTurnStatus.Stopped,
                sessionIdCaptured: sessionIdCaptured,
                streamingMessageUpdated: streamingMessageUpdated,
                shouldClearCurrentStreamingMessage: streamingMessageUpdated);
        }
    }
}
