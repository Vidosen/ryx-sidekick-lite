// SPDX-License-Identifier: GPL-3.0-only
using System;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Parsing;
using Ryx.Sidekick.Editor.Providers;
using UnityEngine;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Routes stream-json events from Claude CLI output to typed handlers.
    /// Implements IStreamEventParser to allow provider-agnostic usage in ProcessManager.
    /// </summary>
    internal class StreamJsonEventRouter : IStreamEventParser
    {
        public event Action<StreamEvent> OnStreamEvent;
        public event Action<string> OnTextDelta;
        public event Action<ToolUse> OnToolUse;
        public event Action<string, string> OnToolResult;
        public event Action<PendingPermission> OnPermissionRequest;
        public event Action<ImageAttachment> OnImageAttachment;
        public event Action<string> OnControlRequest;
        public event Action<string> OnSessionIdReceived;
        public event Action<ResultEvent> OnResult;
        public event Action<string> OnRawLine;

        /// <summary>Fired when thinking block starts streaming.</summary>
        public event Action OnThinkingStarted;
        /// <summary>Fired for each thinking text chunk.</summary>
        public event Action<string> OnThinkingDelta;
        /// <summary>Fired when thinking block completes (with total accumulated thinking text).</summary>
        public event Action<string> OnThinkingCompleted;

        private readonly ToolUseTracker _toolTracker = new(static () => SidekickSettings.instance.VerboseLogging);
        private readonly IProviderToolMapper _toolMapper;

        // Thinking block tracking
        private bool _isThinkingBlock;
        private readonly System.Text.StringBuilder _thinkingBuffer = new();
        private DateTime _thinkingStartTime;

        public ToolUseTracker ToolTracker => _toolTracker;

        public StreamJsonEventRouter(IProviderToolMapper toolMapper = null)
        {
            _toolMapper = toolMapper;
        }

        /// <summary>Returns true if currently processing a thinking block.</summary>
        public bool IsThinkingActive => _isThinkingBlock;

        /// <summary>Gets the elapsed thinking duration in seconds (if thinking is active).</summary>
        public double ThinkingElapsedSeconds =>
            _isThinkingBlock ? (DateTime.Now - _thinkingStartTime).TotalSeconds : 0;

        /// <summary>
        /// Process a single line of CLI output.
        /// </summary>
        public void ProcessLine(string line)
        {
            OnRawLine?.Invoke(line);

            if (string.IsNullOrWhiteSpace(line)) return;

            try
            {
                var baseEvent = JsonUtils.Deserialize<StreamEvent>(line);
                if (baseEvent != null && !string.IsNullOrEmpty(baseEvent.type))
                {
                    DispatchEvent(line, baseEvent.type);
                }
                else
                {
                    // Non-JSON fallback text
                    OnTextDelta?.Invoke(line);
                }
            }
            catch
            {
                // Parse failed - treat as plain text
                OnTextDelta?.Invoke(line);
            }
        }

        private void DispatchEvent(string json, string eventType)
        {
            if (SidekickSettings.instance.VerboseLogging)
            {
                Debug.Log($"[StreamJsonEventRouter] Event: {eventType}");
            }

            try
            {
                switch (eventType)
                {
                    case "stream_event":
                        UnwrapStreamEvent(json);
                        break;

                    case "init":
                        HandleInitEvent(json);
                        break;

                    case "system":
                        HandleSystemEvent(json);
                        break;

                    case "user":
                        HandleUserMessage(json);
                        break;

                    case "assistant":
                        HandleAssistantMessage(json);
                        break;

                    case "content_block_start":
                        HandleContentBlockStart(json);
                        break;

                    case "content_block_delta":
                        HandleContentBlockDelta(json);
                        break;

                    case "content_block_stop":
                        HandleContentBlockStop();
                        break;

                    case "message_start":
                    case "message_delta":
                    case "message_stop":
                        OnStreamEvent?.Invoke(new StreamEvent { type = eventType });
                        break;

                    case "tool_use":
                        OnStreamEvent?.Invoke(new StreamEvent { type = eventType });
                        break;

                    case "tool_result":
                        HandleToolResult(json);
                        break;

                    case "permission_request":
                        HandlePermissionRequest(json);
                        break;

                    case "control_request":
                        OnControlRequest?.Invoke(json);
                        OnStreamEvent?.Invoke(new StreamEvent { type = eventType });
                        break;

                    case "result":
                        HandleResult(json);
                        break;

                    default:
                        if (SidekickSettings.instance.VerboseLogging)
                        {
                            Debug.Log($"[StreamJsonEventRouter] Unhandled event type: {eventType}");
                        }
                        OnStreamEvent?.Invoke(new StreamEvent { type = eventType });
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StreamJsonEventRouter] Failed to parse event '{eventType}': {ex.Message}");
            }
        }

        private void UnwrapStreamEvent(string json)
        {
            try
            {
                var envelope = JObject.Parse(json);
                if (envelope["event"] is JObject inner)
                {
                    var innerType = inner["type"]?.Value<string>();
                    if (!string.IsNullOrEmpty(innerType))
                    {
                        DispatchEvent(inner.ToString(Newtonsoft.Json.Formatting.None), innerType);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StreamJsonEventRouter] Failed to unwrap stream_event: {ex.Message}");
            }
        }

        private void HandleInitEvent(string json)
        {
            var initEvent = JsonUtils.Deserialize<SystemEvent>(json);
            if (!string.IsNullOrEmpty(initEvent?.session_id))
            {
                OnSessionIdReceived?.Invoke(initEvent.session_id);
            }
            OnStreamEvent?.Invoke(initEvent);
        }

        private void HandleSystemEvent(string json)
        {
            var systemEvent = JsonUtils.Deserialize<SystemEvent>(json);
            if (!string.IsNullOrEmpty(systemEvent?.session_id))
            {
                OnSessionIdReceived?.Invoke(systemEvent.session_id);
            }
            OnStreamEvent?.Invoke(systemEvent);
        }

        private void HandleUserMessage(string json)
        {
            try
            {
                var jObj = JObject.Parse(json);
                var messageContent = jObj["message"]?["content"];

                if (messageContent is JArray content)
                {
                    foreach (var block in content)
                    {
                        if (block["type"]?.Value<string>() == "tool_result")
                        {
                            var toolUseId = block["tool_use_id"]?.Value<string>();
                            var resultContent = block["content"]?.Value<string>();
                            if (!string.IsNullOrEmpty(toolUseId))
                            {
                                OnToolResult?.Invoke(toolUseId, resultContent ?? "");
                            }
                        }
                    }
                }
                else if (messageContent?.Type == JTokenType.String)
                {
                    // Handle local command output (e.g., /cost, /context)
                    var text = messageContent.Value<string>();
                    if (!string.IsNullOrEmpty(text))
                    {
                        // Strip local command tags using shared parser
                        text = CommandTagParser.ExtractLocalCommandOutput(text);
                        if (!string.IsNullOrEmpty(text))
                        {
                            OnTextDelta?.Invoke(text);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[StreamJsonEventRouter] Failed to parse user message: {ex.Message}");
                }
            }

            OnStreamEvent?.Invoke(new StreamEvent { type = "user" });
        }

        private void HandleAssistantMessage(string json)
        {
            var assistantEvent = JsonUtils.Deserialize<AssistantMessageEvent>(json);
            OnStreamEvent?.Invoke(assistantEvent);

            if (assistantEvent?.message?.content == null) return;

            var hadStreamedText = _toolTracker.CurrentResponseLength > 0;

            foreach (var block in assistantEvent.message.content)
            {
                switch (block.type)
                {
                    case "text":
                        if (!hadStreamedText && !string.IsNullOrEmpty(block.text))
                        {
                            OnTextDelta?.Invoke(block.text);
                        }
                        break;

                    case "tool_use":
                        if (!_toolTracker.HasPendingToolUse(block.id))
                        {
                            var toolUse = new ToolUse
                            {
                                Id = block.id,
                                Name = block.name,
                                RawName = block.name,
                                Input = block.input,
                                Status = ToolStatus.Running
                            };
                            _toolMapper?.Normalize(toolUse);
                            _toolTracker.RegisterToolUse(toolUse);
                            OnToolUse?.Invoke(toolUse);
                        }
                        break;

                    case "image":
                    case "input_image":
                    case "inline_image":
                    case "image_attachment":
                        var attachment = ParseImageAttachment(block);
                        if (attachment != null)
                        {
                            OnImageAttachment?.Invoke(attachment);
                        }
                        break;
                }
            }
        }

        private void HandleContentBlockStart(string json)
        {
            try
            {
                var jObj = JObject.Parse(json);
                var contentBlock = jObj["content_block"];
                var index = jObj["index"]?.Value<int>() ?? 0;
                var blockType = contentBlock?["type"]?.Value<string>();

                switch (blockType)
                {
                    case "tool_use":
                        _toolTracker.StartToolBlock(
                            contentBlock["id"]?.Value<string>(),
                            contentBlock["name"]?.Value<string>(),
                            index);
                        break;

                    case "thinking":
                        _isThinkingBlock = true;
                        _thinkingBuffer.Clear();
                        _thinkingStartTime = DateTime.Now;
                        OnThinkingStarted?.Invoke();
                        break;

                    case "redacted_thinking":
                        // Redacted thinking blocks have no content to stream, just note it started
                        _isThinkingBlock = true;
                        _thinkingBuffer.Clear();
                        _thinkingStartTime = DateTime.Now;
                        OnThinkingStarted?.Invoke();
                        break;
                }

                OnStreamEvent?.Invoke(new StreamEvent { type = "content_block_start" });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StreamJsonEventRouter] Failed to parse content_block_start: {ex.Message}");
            }
        }

        private void HandleContentBlockDelta(string json)
        {
            try
            {
                var deltaEvent = JsonUtils.Deserialize<ContentBlockDeltaEvent>(json);
                OnStreamEvent?.Invoke(deltaEvent);

                if (deltaEvent?.delta != null)
                {
                    var deltaType = deltaEvent.delta.type;

                    switch (deltaType)
                    {
                        case "text_delta":
                            if (!string.IsNullOrEmpty(deltaEvent.delta.text))
                            {
                                _toolTracker.AppendResponse(deltaEvent.delta.text);
                                OnTextDelta?.Invoke(deltaEvent.delta.text);
                            }
                            break;

                        case "thinking_delta":
                            if (!string.IsNullOrEmpty(deltaEvent.delta.thinking))
                            {
                                _thinkingBuffer.Append(deltaEvent.delta.thinking);
                                OnThinkingDelta?.Invoke(deltaEvent.delta.thinking);
                            }
                            break;

                        case "signature_delta":
                            // Signature is for integrity verification, we can ignore in UI
                            break;

                        case "input_json_delta":
                            if (!string.IsNullOrEmpty(deltaEvent.delta.partial_json))
                            {
                                _toolTracker.AppendToolInput(deltaEvent.delta.partial_json);
                            }
                            break;

                        default:
                            // Fallback for unnamed text deltas (older format)
                            if (!string.IsNullOrEmpty(deltaEvent.delta.text))
                            {
                                _toolTracker.AppendResponse(deltaEvent.delta.text);
                                OnTextDelta?.Invoke(deltaEvent.delta.text);
                            }
                            if (!string.IsNullOrEmpty(deltaEvent.delta.partial_json))
                            {
                                _toolTracker.AppendToolInput(deltaEvent.delta.partial_json);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StreamJsonEventRouter] Failed to parse content_block_delta: {ex.Message}");
            }
        }

        private void HandleContentBlockStop()
        {
            OnStreamEvent?.Invoke(new StreamEvent { type = "content_block_stop" });

            // Complete thinking block if active
            if (_isThinkingBlock)
            {
                var thinkingText = _thinkingBuffer.ToString();
                _isThinkingBlock = false;
                OnThinkingCompleted?.Invoke(thinkingText);
            }

            // Complete tool block if active
            var toolUse = _toolTracker.CompleteToolBlock();
            if (toolUse != null)
            {
                _toolMapper?.Normalize(toolUse);
                OnToolUse?.Invoke(toolUse);
            }
        }

        private void HandleToolResult(string json)
        {
            var toolResultEvent = JsonUtils.Deserialize<ToolResultEvent>(json);
            OnStreamEvent?.Invoke(toolResultEvent);

            if (!string.IsNullOrEmpty(toolResultEvent?.tool_use_id))
            {
                OnToolResult?.Invoke(toolResultEvent.tool_use_id, toolResultEvent.content ?? "");
            }
        }

        private void HandlePermissionRequest(string json)
        {
            var permEvent = JsonUtils.Deserialize<PermissionRequestEvent>(json);
            if (permEvent != null)
            {
                var pending = new PendingPermission
                {
                    ToolUseId = permEvent.ToolUseId,
                    ToolName = permEvent.ToolName,
                    RawToolName = permEvent.ToolName,
                    FilePath = permEvent.FilePath,
                    Input = permEvent.Input,
                    SessionId = permEvent.SessionId,
                    RawInput = permEvent.Input?.ToString()
                };
                _toolMapper?.Normalize(pending);
                OnPermissionRequest?.Invoke(pending);
            }
            OnStreamEvent?.Invoke(new StreamEvent { type = "permission_request" });
        }

        private void HandleResult(string json)
        {
            var resultEvent = JsonUtils.Deserialize<ResultEvent>(json);
            if (!string.IsNullOrEmpty(resultEvent?.session_id))
            {
                OnSessionIdReceived?.Invoke(resultEvent.session_id);
            }
            OnStreamEvent?.Invoke(resultEvent);
            OnResult?.Invoke(resultEvent);
        }

        private static ImageAttachment ParseImageAttachment(ContentBlock block)
        {
            if (block == null) return null;

            var data = block.data ??
                       block.source?["data"]?.Value<string>() ??
                       block.input?["data"]?.Value<string>();

            if (string.IsNullOrEmpty(data)) return null;

            var mediaType = block.media_type ??
                            block.mime_type ??
                            block.source?["media_type"]?.Value<string>() ??
                            "image/png";

            // Strip data URL prefix if present
            const string marker = "base64,";
            var idx = data.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            var cleanData = idx >= 0 ? data.Substring(idx + marker.Length) : data;

            return new ImageAttachment
            {
                Id = block.id ?? Guid.NewGuid().ToString(),
                MediaType = mediaType,
                Data = cleanData,
                FileName = block.file_name ?? block.name
            };
        }

        /// <summary>
        /// Clear accumulated state for a new turn.
        /// </summary>
        public void Reset()
        {
            _toolTracker.Reset();
            _isThinkingBlock = false;
            _thinkingBuffer.Clear();
        }
    }
}
