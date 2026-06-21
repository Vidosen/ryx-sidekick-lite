// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Chat
{
    internal enum RuntimeEventKind
    {
        TurnStarted,
        TurnFinished,
        AssistantMessageStarted,
        TextDelta,
        ImageAttachmentReceived,
        ToolUseReceived,
        ToolResultReceived,
        ThinkingStarted,
        ThinkingDelta,
        ThinkingCompleted,
        ContextUsageUpdated,
        StreamCompleted,
        ErrorOccurred
    }

    internal sealed class HandleRuntimeEventRequest
    {
        public RuntimeEventKind Kind { get; set; }
        public string Text { get; set; }
        public ToolUse ToolUse { get; set; }
        public string ToolUseId { get; set; }
        public string ToolResultContent { get; set; }
        public ImageAttachment ImageAttachment { get; set; }
        public int UsedTokens { get; set; }
        public int ContextWindow { get; set; }
        public string SessionId { get; set; }
    }

    internal sealed class HandleRuntimeEventResult
    {
        public List<Message> AddedMessages { get; } = new();

        public List<string> UpdatedMessageIds { get; } = new();

        public List<string> UpdatedToolUseIds { get; } = new();

        public bool ShouldRefreshUi { get; set; }

        public bool ShouldResetPendingPermissions { get; set; }

        public bool ShouldNotifyAssetRefreshOnStreamComplete { get; set; }

        public bool? TurnActiveChanged { get; set; }

        public RuntimeContextUsageResult ContextUsage { get; set; }

        public ToolUse ToolUseForAssetRefresh { get; set; }

        public ToolUse ToolResultForAssetRefresh { get; set; }

        internal void AddUpdatedMessageId(string messageId)
        {
            if (!string.IsNullOrEmpty(messageId) && !UpdatedMessageIds.Contains(messageId))
            {
                UpdatedMessageIds.Add(messageId);
            }
        }

        internal void AddUpdatedToolUseId(string toolUseId)
        {
            if (!string.IsNullOrEmpty(toolUseId) && !UpdatedToolUseIds.Contains(toolUseId))
            {
                UpdatedToolUseIds.Add(toolUseId);
            }
        }
    }

    internal sealed class RuntimeContextUsageResult
    {
        public RuntimeContextUsageResult(int usedTokens, int contextWindow)
        {
            UsedTokens = usedTokens;
            ContextWindow = contextWindow;
        }

        public int UsedTokens { get; }

        public int ContextWindow { get; }
    }

    internal sealed class HandleRuntimeEventUseCase
    {
        private readonly ChatSessionState _chatSessionState;
        private readonly TurnStreamAccumulator _accumulator;
        private readonly IClock _clock;
        private readonly ISettingsStore _settingsStore;

        public HandleRuntimeEventUseCase(
            ChatSessionState chatSessionState,
            TurnStreamAccumulator accumulator,
            IClock clock,
            ISettingsStore settingsStore)
        {
            _chatSessionState = chatSessionState;
            _accumulator = accumulator ?? throw new ArgumentNullException(nameof(accumulator));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _settingsStore = settingsStore;
        }

        public HandleRuntimeEventResult Handle(HandleRuntimeEventRequest request)
        {
            var result = new HandleRuntimeEventResult();

            switch (request?.Kind)
            {
                case RuntimeEventKind.TurnStarted:
                    HandleTurnStarted(result);
                    break;
                case RuntimeEventKind.TurnFinished:
                    HandleTurnFinished(result);
                    break;
                case RuntimeEventKind.AssistantMessageStarted:
                    HandleAssistantMessageStarted(result);
                    break;
                case RuntimeEventKind.TextDelta:
                    HandleTextDelta(request.Text, result);
                    break;
                case RuntimeEventKind.ImageAttachmentReceived:
                    HandleImageAttachment(request.ImageAttachment, result);
                    break;
                case RuntimeEventKind.ToolUseReceived:
                    HandleToolUse(request.ToolUse, result);
                    break;
                case RuntimeEventKind.ToolResultReceived:
                    HandleToolResult(request.ToolUseId, request.ToolResultContent, result);
                    break;
                case RuntimeEventKind.ThinkingStarted:
                    HandleThinkingStarted(result);
                    break;
                case RuntimeEventKind.ThinkingDelta:
                    HandleThinkingDelta(request.Text, result);
                    break;
                case RuntimeEventKind.ThinkingCompleted:
                    HandleThinkingCompleted(request.Text, result);
                    break;
                case RuntimeEventKind.ContextUsageUpdated:
                    result.ContextUsage = new RuntimeContextUsageResult(request.UsedTokens, request.ContextWindow);
                    break;
                case RuntimeEventKind.StreamCompleted:
                    HandleStreamCompleted(request.SessionId, result);
                    break;
                case RuntimeEventKind.ErrorOccurred:
                    HandleError(request.Text, result);
                    break;
            }

            return result;
        }

        private void HandleTurnStarted(HandleRuntimeEventResult result)
        {
            _accumulator.ClearTurnBuffers();
            result.TurnActiveChanged = true;

            var conversation = _chatSessionState?.CurrentConversation;
            if (conversation == null)
            {
                return;
            }

            EnsureStreamingMessage(conversation, result);
        }

        private void HandleTurnFinished(HandleRuntimeEventResult result)
        {
            if (_accumulator.CurrentStreamingMessage != null)
            {
                _accumulator.CurrentStreamingMessage.IsStreaming = false;
            }

            result.TurnActiveChanged = false;
            result.ShouldRefreshUi = true;
        }

        private void HandleAssistantMessageStarted(HandleRuntimeEventResult result)
        {
            if (_accumulator.CurrentStreamingMessage == null || _accumulator.StreamingContent.Length == 0)
            {
                return;
            }

            FinalizeCurrentTextBlock();
            result.ShouldRefreshUi = true;
        }

        private void HandleTextDelta(string delta, HandleRuntimeEventResult result)
        {
            if (_accumulator.CurrentStreamingMessage == null)
            {
                var conversation = _chatSessionState?.CurrentConversation;
                if (conversation == null)
                {
                    return;
                }

                _accumulator.StreamingContent.Clear();
                _accumulator.CurrentStreamingMessage = new Message
                {
                    Role = MessageRole.Assistant,
                    IsStreaming = true
                };
                conversation.Messages.Add(_accumulator.CurrentStreamingMessage);
                result.AddedMessages.Add(_accumulator.CurrentStreamingMessage);
            }

            _accumulator.StreamingContent.Append(delta);
            _accumulator.CurrentStreamingMessage.Content = _accumulator.StreamingContent.ToString();
            result.AddUpdatedMessageId(_accumulator.CurrentStreamingMessage.Id);
        }

        private void HandleImageAttachment(ImageAttachment attachment, HandleRuntimeEventResult result)
        {
            if (attachment == null)
            {
                return;
            }

            var (conversation, _) = _chatSessionState?.EnsureConversation() ?? (null, false);
            if (conversation == null)
            {
                return;
            }

            EnsureStreamingMessage(conversation, result);
            _accumulator.CurrentStreamingMessage.Attachments ??= new List<ImageAttachment>();
            _accumulator.CurrentStreamingMessage.Attachments.Add(attachment);
            result.AddUpdatedMessageId(_accumulator.CurrentStreamingMessage.Id);
        }

        private void HandleToolUse(ToolUse toolUse, HandleRuntimeEventResult result)
        {
            var conversation = _chatSessionState?.CurrentConversation;
            if (conversation == null || toolUse == null)
            {
                return;
            }

            if (_accumulator.ActiveTools.TryGetValue(toolUse.Id, out var existingTool))
            {
                MergeToolUse(existingTool, toolUse);
                EnrichToolMetadata(existingTool);
                result.AddUpdatedToolUseId(existingTool.Id);
                result.ToolUseForAssetRefresh = existingTool;
                return;
            }

            FinalizeCurrentTextBlock();
            EnsureStreamingMessage(conversation, result);

            if (toolUse.Status == ToolStatus.Pending)
            {
                toolUse.Status = ToolStatus.Running;
            }

            toolUse.IsStreaming = toolUse.Status == ToolStatus.Running || toolUse.Status == ToolStatus.Pending;
            EnrichToolMetadata(toolUse);
            _accumulator.ActiveTools[toolUse.Id] = toolUse;

            var toolMessage = new Message
            {
                Role = MessageRole.Tool,
                Content = string.Empty,
                Timestamp = _clock.Now
            };
            toolMessage.ToolUses.Add(toolUse);

            _accumulator.ToolMessages[toolUse.Id] = toolMessage;

            var streamingIndex = conversation.Messages.IndexOf(_accumulator.CurrentStreamingMessage);
            if (streamingIndex >= 0)
            {
                conversation.Messages.Insert(streamingIndex, toolMessage);
            }
            else
            {
                conversation.Messages.Add(toolMessage);
            }

            result.AddedMessages.Add(toolMessage);
            result.ToolUseForAssetRefresh = toolUse;
        }

        private void HandleToolResult(string toolUseId, string content, HandleRuntimeEventResult result)
        {
            if (string.IsNullOrEmpty(toolUseId))
            {
                return;
            }

            if (_accumulator.ActiveTools.TryGetValue(toolUseId, out var tool))
            {
                tool.Output = (tool.Output ?? string.Empty) + content;
                tool.IsStreaming = false;
                tool.Status = ToolStatus.Success;
                result.AddUpdatedToolUseId(toolUseId);
                result.ToolResultForAssetRefresh = tool;
            }
        }

        private void HandleThinkingStarted(HandleRuntimeEventResult result)
        {
            FinalizeCurrentTextBlock();

            _accumulator.IsThinkingActive = true;
            _accumulator.ThinkingContent.Clear();
            _accumulator.ThinkingStartTime = _clock.Now;

            var conversation = _chatSessionState?.CurrentConversation;
            if (conversation == null)
            {
                return;
            }

            _accumulator.CurrentThinkingMessage = new Message
            {
                Role = MessageRole.Assistant,
                IsThinkingBlock = true,
                IsStreaming = true,
                ThinkingContent = string.Empty,
                Content = string.Empty
            };

            EnsureStreamingMessage(conversation, result);

            var streamingIndex = conversation.Messages.IndexOf(_accumulator.CurrentStreamingMessage);
            if (streamingIndex >= 0)
            {
                conversation.Messages.Insert(streamingIndex, _accumulator.CurrentThinkingMessage);
            }
            else
            {
                conversation.Messages.Add(_accumulator.CurrentThinkingMessage);
            }

            result.AddedMessages.Add(_accumulator.CurrentThinkingMessage);
        }

        private void HandleThinkingDelta(string chunk, HandleRuntimeEventResult result)
        {
            if (_accumulator.CurrentThinkingMessage == null)
            {
                return;
            }

            _accumulator.ThinkingContent.Append(chunk);
            var thinkingText = _accumulator.ThinkingContent.ToString();
            _accumulator.CurrentThinkingMessage.ThinkingContent = thinkingText;
            _accumulator.CurrentThinkingMessage.Content = thinkingText;
            result.AddUpdatedMessageId(_accumulator.CurrentThinkingMessage.Id);
        }

        private void HandleThinkingCompleted(string completedText, HandleRuntimeEventResult result)
        {
            _accumulator.IsThinkingActive = false;

            if (_accumulator.CurrentThinkingMessage != null)
            {
                var elapsed = (_clock.Now - _accumulator.ThinkingStartTime).TotalSeconds;
                var thinkingText = _accumulator.ThinkingContent.Length > 0
                    ? _accumulator.ThinkingContent.ToString()
                    : completedText ?? string.Empty;
                _accumulator.CurrentThinkingMessage.ThinkingContent = thinkingText;
                _accumulator.CurrentThinkingMessage.Content = thinkingText;
                _accumulator.CurrentThinkingMessage.ThinkingDurationSeconds = elapsed;
                _accumulator.CurrentThinkingMessage.IsStreaming = false;
                result.AddUpdatedMessageId(_accumulator.CurrentThinkingMessage.Id);
            }

            _accumulator.CurrentThinkingMessage = null;
        }

        private void HandleStreamCompleted(string sessionId, HandleRuntimeEventResult result)
        {
            if (_accumulator.CurrentStreamingMessage != null)
            {
                _accumulator.CurrentStreamingMessage.IsStreaming = false;
            }

            if (_accumulator.CurrentThinkingMessage != null)
            {
                _accumulator.CurrentThinkingMessage.IsStreaming = false;
            }

            foreach (var tool in _accumulator.ActiveTools.Values)
            {
                if (!tool.IsStreaming)
                {
                    continue;
                }

                tool.IsStreaming = false;
                if (tool.Status == ToolStatus.Running)
                {
                    tool.Status = ToolStatus.Success;
                }

                result.AddUpdatedToolUseId(tool.Id);
            }

            _chatSessionState?.ApplyRuntimeSessionId(sessionId);

            _accumulator.ResetAfterStreamComplete();
            result.ShouldResetPendingPermissions = true;
            result.ShouldNotifyAssetRefreshOnStreamComplete = true;
            result.ShouldRefreshUi = true;
        }

        private void HandleError(string error, HandleRuntimeEventResult result)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return;
            }

            var normalizedError = error.Trim();
            var (conversation, _) = _chatSessionState?.EnsureConversation() ?? (null, false);
            if (conversation == null)
            {
                return;
            }

            var systemMessage = new Message
            {
                Role = MessageRole.System,
                Content = $"[ERROR] {normalizedError}",
                Timestamp = _clock.Now
            };

            conversation.Messages.Add(systemMessage);
            conversation.UpdatedAt = _clock.Now;

            result.AddedMessages.Add(systemMessage);
            result.ShouldRefreshUi = true;
        }

        private void EnsureStreamingMessage(Conversation conversation, HandleRuntimeEventResult result)
        {
            _accumulator.CurrentStreamingMessage ??= new Message
            {
                Role = MessageRole.Assistant,
                IsStreaming = true
            };

            if (!conversation.Messages.Contains(_accumulator.CurrentStreamingMessage))
            {
                conversation.Messages.Add(_accumulator.CurrentStreamingMessage);
                result.AddedMessages.Add(_accumulator.CurrentStreamingMessage);
            }
        }

        private void FinalizeCurrentTextBlock()
        {
            if (_accumulator.CurrentStreamingMessage != null && _accumulator.StreamingContent.Length > 0)
            {
                _accumulator.CurrentStreamingMessage.IsStreaming = false;
                _accumulator.CurrentStreamingMessage = null;
                _accumulator.StreamingContent.Clear();
            }
        }

        private void EnrichToolMetadata(ToolUse toolUse)
        {
            if (toolUse == null)
            {
                return;
            }

            var toolKind = ToolPresentationCatalog.GetEffectiveKind(toolUse);

            if (string.IsNullOrEmpty(toolUse.FilePath))
            {
                toolUse.FilePath = ExtractInputString(toolUse.Input, "file_path")
                    ?? ExtractInputString(toolUse.Input, "filePath")
                    ?? ExtractInputString(toolUse.Input, "path");
            }

            if (!string.IsNullOrEmpty(toolUse.DiffContent))
            {
                return;
            }

            if (toolKind == ToolKind.Write)
            {
                toolUse.DiffContent = BuildSyntheticWriteDiff(toolUse.FilePath, ExtractInputString(toolUse.Input, "content"));
                return;
            }

            if (toolKind == ToolKind.Edit)
            {
                var oldText = ExtractInputString(toolUse.Input, "old_string");
                var newText = ExtractInputString(toolUse.Input, "new_string");
                toolUse.DiffContent = BuildSyntheticEditDiff(toolUse.FilePath, oldText, newText);
            }
        }

        private static void MergeToolUse(ToolUse target, ToolUse update)
        {
            if (target == null || update == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(update.Name))
            {
                target.Name = update.Name;
            }

            if (update.Input != null)
            {
                target.Input = update.Input.DeepClone();
            }

            if (update.Output != null)
            {
                target.Output = update.Output;
            }

            target.Status = update.Status;
            target.IsStreaming = update.IsStreaming;

            if (!string.IsNullOrWhiteSpace(update.FilePath))
            {
                target.FilePath = update.FilePath;
            }

            if (!string.IsNullOrWhiteSpace(update.DiffContent))
            {
                target.DiffContent = update.DiffContent;
            }

            if (!string.IsNullOrWhiteSpace(update.CommandLine))
            {
                target.CommandLine = update.CommandLine;
            }

            if (!string.IsNullOrWhiteSpace(update.Description))
            {
                target.Description = update.Description;
            }
        }

        private static string BuildSyntheticWriteDiff(string filePath, string content)
        {
            var addedLineCount = CountLines(content);
            if (addedLineCount == 0)
            {
                return null;
            }

            var normalizedPath = NormalizeDiffPath(filePath);
            var sb = new StringBuilder();
            sb.AppendLine("--- /dev/null");
            sb.AppendLine($"+++ b/{normalizedPath}");
            sb.AppendLine($"@@ -0,0 +1,{addedLineCount} @@");
            for (var i = 0; i < addedLineCount; i++)
            {
                sb.AppendLine("+");
            }

            return sb.ToString();
        }

        private string BuildSyntheticEditDiff(string filePath, string oldText, string newText)
        {
            if (string.Equals(oldText, newText, StringComparison.Ordinal))
            {
                return null;
            }

            var removedLineCount = CountLines(oldText);
            var addedLineCount = CountLines(newText);
            if (removedLineCount == 0 && addedLineCount == 0)
            {
                return null;
            }

            var normalizedPath = NormalizeDiffPath(filePath);
            var startLine = EstimateEditStartLine(filePath, oldText);

            var sb = new StringBuilder();
            sb.AppendLine($"--- a/{normalizedPath}");
            sb.AppendLine($"+++ b/{normalizedPath}");
            sb.AppendLine($"@@ -{startLine},{removedLineCount} +{startLine},{addedLineCount} @@");

            for (var i = 0; i < removedLineCount; i++)
            {
                sb.AppendLine("-");
            }

            for (var i = 0; i < addedLineCount; i++)
            {
                sb.AppendLine("+");
            }

            return sb.ToString();
        }

        private int EstimateEditStartLine(string filePath, string oldText)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(oldText))
            {
                return 1;
            }

            try
            {
                if (_settingsStore == null || string.IsNullOrEmpty(_settingsStore.WorkingDirectory))
                {
                    return 1;
                }

                var fullPath = Path.Combine(_settingsStore.WorkingDirectory, filePath);
                if (!File.Exists(fullPath))
                {
                    return 1;
                }

                var fileContent = File.ReadAllText(fullPath);
                if (string.IsNullOrEmpty(fileContent))
                {
                    return 1;
                }

                var charIndex = fileContent.IndexOf(oldText, StringComparison.Ordinal);
                if (charIndex >= 0)
                {
                    return CountLineBreaks(fileContent, charIndex) + 1;
                }

                var normalizedFile = fileContent.Replace("\r\n", "\n");
                var normalizedOld = oldText.Replace("\r\n", "\n");
                charIndex = normalizedFile.IndexOf(normalizedOld, StringComparison.Ordinal);
                return charIndex >= 0 ? CountLineBreaks(normalizedFile, charIndex) + 1 : 1;
            }
            catch
            {
                return 1;
            }
        }

        private static int CountLineBreaks(string text, int length)
        {
            if (string.IsNullOrEmpty(text) || length <= 0)
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < length && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
        }

        private static string ExtractInputString(JToken input, string key)
        {
            if (input == null || string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (input.Type == JTokenType.Object)
            {
                return input[key]?.ToString();
            }

            if (input.Type != JTokenType.String)
            {
                return null;
            }

            try
            {
                var parsed = JToken.Parse(input.ToString());
                return parsed.Type == JTokenType.Object ? parsed[key]?.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeDiffPath(string filePath)
        {
            return string.IsNullOrEmpty(filePath) ? "unknown" : filePath.Replace('\\', '/');
        }
    }
}
