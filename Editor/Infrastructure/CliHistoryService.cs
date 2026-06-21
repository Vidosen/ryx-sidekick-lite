// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Parsing;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using Ryx.Sidekick.Editor.UseCases.Questions;
using UnityEngine;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Service for reading conversation history from Claude CLI's native storage.
    /// CLI stores conversations in ~/.claude/projects/{project-dir}/*.jsonl
    /// </summary>
    internal static class CliHistoryService
    {
        // Regex to find content field and check if it's a string or array
        private static readonly Regex ContentFieldRegex = new(@"""content""\s*:\s*", RegexOptions.Compiled);
        
        // Regex to extract a JSON string value (used after finding content field position)
        private static readonly Regex JsonStringRegex = new(@"^""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
        
        // Regex to extract text from content block array (for messages with content as array)
        private static readonly Regex BlockTextRegex = new(
            @"""type""\s*:\s*""text""[^}]*""text""\s*:\s*""((?:[^""\\]|\\.)*)""",
            RegexOptions.Compiled);
        
        private static readonly Regex NonAlphaNumericRegex = new(@"[^a-zA-Z0-9]", RegexOptions.Compiled);
        /// <summary>
        /// Gets the CLI storage directory for the current Unity project.
        /// </summary>
        public static string GetCliStoragePath()
        {
            var projectPath = GetProjectPath();
            var mappedDir = MapPathToDirectoryName(projectPath);
            var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userHome, ".claude", "projects", mappedDir);
        }

        /// <summary>
        /// Gets the Unity project path (parent of Assets folder).
        /// </summary>
        private static string GetProjectPath()
        {
            // Application.dataPath returns {ProjectPath}/Assets
            var assetsPath = Application.dataPath;
            return Path.GetDirectoryName(assetsPath);
        }

        /// <summary>
        /// Maps a file path to CLI's directory naming convention.
        /// /Users/vidosen/Projects/my-project → -Users-vidosen-Projects-my-project
        /// Note: The leading dash IS part of the directory name in CLI storage.
        /// </summary>
        private static string MapPathToDirectoryName(string path)
        {
            // Match CLI's naming: replace any non-alphanumeric character with '-'
            // The leading dash from root paths (e.g., /Users/...) is preserved
            return NonAlphaNumericRegex.Replace(path, "-");
        }

        /// <summary>
        /// Lists all available sessions from CLI storage.
        /// Returns sessions sorted by last modified date (newest first).
        /// </summary>
        public static List<CliSessionInfo> ListSessions()
        {
            var sessions = new List<CliSessionInfo>();
            var storagePath = GetCliStoragePath();

            if (!Directory.Exists(storagePath))
                return sessions;

            try
            {
                var jsonlFiles = Directory.GetFiles(storagePath, "*.jsonl");
                
                foreach (var filePath in jsonlFiles)
                {
                    var sessionInfo = ParseSessionFile(filePath);
                    if (sessionInfo != null)
                    {
                        sessions.Add(sessionInfo);
                    }
                }

                // Deduplicate by sessionId and sort newest first
                sessions = DeduplicateSessions(sessions);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ryx Sidekick] Failed to list CLI sessions: {ex.Message}");
            }

            return sessions;
        }

        /// <summary>
        /// Parses a session JSONL file to extract metadata (without loading full history).
        /// </summary>
        internal static CliSessionInfo ParseSessionFile(string filePath)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);

                // Skip agent/sidechain runs that should not appear as standalone chats
                if (fileName.StartsWith("agent-"))
                {
                    return null;
                }

                var fileInfo = new FileInfo(filePath);
                
                // Skip empty files
                if (fileInfo.Length == 0)
                {
                    return null;
                }

                var sessionInfo = new CliSessionInfo
                {
                    SessionId = fileName,
                    FilePath = filePath,
                    UpdatedAt = fileInfo.LastWriteTime,
                    CreatedAt = fileInfo.CreationTime
                };

                string detectedSessionId = null;
                var hasConversationText = false;

                // Read first few lines to get title and session metadata
                using (var reader = new StreamReader(filePath))
                {
                    while (reader.ReadLine() is { } line && string.IsNullOrEmpty(sessionInfo.Title))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var entry = JsonUtils.Deserialize<CliSessionEntry>(line);
                            
                            // Capture embedded sessionId if present
                            if (!string.IsNullOrEmpty(entry.sessionId))
                            {
                                detectedSessionId ??= entry.sessionId;
                            }
                            
                            // Get title from first user message
                            if (entry.type == "user" && entry.message != null)
                            {
                                // Extract content - try block array first, then raw JSON for string content
                                var content = ExtractMessageContent(entry.message);
                                if (string.IsNullOrEmpty(content))
                                {
                                    content = ExtractContentFromJsonLine(line);
                                }

                                if (!string.IsNullOrEmpty(content) &&
                                    ContextAttachmentParser.TryExtractContext(content, out var cleanedTitle, out var parsed))
                                {
                                    content = cleanedTitle;
                                    if (parsed.Count > 0)
                                    {
                                        hasConversationText = true;
                                    }
                                }

                                // Clean local command tags for titles
                                if (!string.IsNullOrEmpty(content) &&
                                    CommandTagParser.TryCleanLocalCommandTags(content, out var cleanedLocalTags))
                                {
                                    content = cleanedLocalTags;
                                }

                                if (!string.IsNullOrEmpty(content) &&
                                    CommandTagParser.TryFormatCommandText(content, out var formattedTitle))
                                {
                                    content = formattedTitle;
                                }
                                
                                if (!string.IsNullOrEmpty(content))
                                {
                                    hasConversationText = true;
                                    sessionInfo.Title = content.Length > 60 
                                        ? content.Substring(0, 60) + "..." 
                                        : content;
                                }
                                
                                // Parse timestamp
                                if (!string.IsNullOrEmpty(entry.timestamp))
                                {
                                    if (DateTime.TryParse(entry.timestamp, out var ts))
                                    {
                                        sessionInfo.CreatedAt = ts;
                                    }
                                }
                            }

                            // Track any assistant/user text even if title already set
                            if (!hasConversationText && entry.message != null &&
                                entry.type is "assistant" or "user")
                            {
                                var content = ExtractMessageContent(entry.message);
                                if (string.IsNullOrEmpty(content))
                                {
                                    content = ExtractContentFromJsonLine(line);
                                }
                                if (!string.IsNullOrEmpty(content) &&
                                    ContextAttachmentParser.TryExtractContext(content, out var cleaned, out var parsed))
                                {
                                    content = cleaned;
                                    if (parsed.Count > 0)
                                    {
                                        hasConversationText = true;
                                    }
                                }

                                // Clean local command tags
                                if (!string.IsNullOrEmpty(content) &&
                                    CommandTagParser.TryCleanLocalCommandTags(content, out var cleanedLocalTags))
                                {
                                    content = cleanedLocalTags;
                                }

                                if (!string.IsNullOrEmpty(content) &&
                                    CommandTagParser.TryFormatCommandText(content, out var formatted))
                                {
                                    content = formatted;
                                }
                                if (!string.IsNullOrEmpty(content))
                                {
                                    hasConversationText = true;
                                }
                            }
                        }
                        catch
                        {
                            // Skip malformed lines
                        }
                    }
                }

                // Prefer embedded sessionId when available
                if (!string.IsNullOrEmpty(detectedSessionId))
                {
                    sessionInfo.SessionId = detectedSessionId;
                }

                // Skip sessions with no meaningful text (queue/sidechain only)
                if (!hasConversationText)
                {
                    return null;
                }

                // Default title if none found
                if (string.IsNullOrEmpty(sessionInfo.Title))
                {
                    sessionInfo.Title = $"Session {fileName[..Math.Min(8, fileName.Length)]}...";
                }

                return sessionInfo;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ryx Sidekick] Failed to parse session file {filePath}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Loads full conversation history from a session file.
        /// </summary>
        public static Conversation LoadConversation(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return null;
            }
            
            var storagePath = GetCliStoragePath();
            var filePath = Path.Combine(storagePath, $"{sessionId}.jsonl");

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[Ryx Sidekick] Session file not found: {filePath}");
                return null;
            }

            try
            {
                var conversation = new Conversation
                {
                    Id = sessionId,
                    SessionId = sessionId,
                    Title = "Loading..."
                };

                var lines = File.ReadAllLines(filePath);
                var fileInfo = new FileInfo(filePath);
                conversation.UpdatedAt = fileInfo.LastWriteTime;
                conversation.CreatedAt = fileInfo.CreationTime;
                var toolUseIndex = new Dictionary<string, ToolUse>();
                
                // Track pending thinking block to calculate duration from timestamps
                Message pendingThinkingMessage = null;
                DateTime? pendingThinkingTimestamp = null;

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var entry = JsonUtils.Deserialize<CliSessionEntry>(line);
                        ProcessSessionEntry(entry, conversation, line, toolUseIndex,
                            ref pendingThinkingMessage, ref pendingThinkingTimestamp);
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }

                // Ensure any tools without explicit results are marked complete
                foreach (var tool in toolUseIndex.Values)
                {
                    if (tool.Status == ToolStatus.Running)
                    {
                        tool.Status = ToolStatus.Success;
                        tool.IsStreaming = false;
                    }
                }

                // Set title from first user message if not already set
                if (conversation.Title == "Loading..." && conversation.Messages.Count > 0)
                {
                    var firstUserMsg = conversation.Messages.FirstOrDefault(m => m.Role == MessageRole.User);
                    if (firstUserMsg != null && !string.IsNullOrEmpty(firstUserMsg.Content))
                    {
                        conversation.Title = firstUserMsg.Content.Length > 60
                            ? firstUserMsg.Content.Substring(0, 60) + "..."
                            : firstUserMsg.Content;
                    }
                    else
                    {
                        conversation.Title = $"Session {sessionId.Substring(0, Math.Min(8, sessionId.Length))}";
                    }
                }

                return conversation;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Ryx Sidekick] Failed to load conversation {sessionId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Processes a single JSONL entry and adds it to the conversation.
        /// </summary>
        internal static void ProcessSessionEntry(
            CliSessionEntry entry, 
            Conversation conversation, 
            string rawJsonLine,
            Dictionary<string, ToolUse> toolUseIndex,
            ref Message pendingThinkingMessage,
            ref DateTime? pendingThinkingTimestamp)
        {
            if (entry == null) return;

            var timestamp = ParseTimestamp(entry.timestamp);
            var toolResultPayload = ExtractToolUseResult(rawJsonLine);
            var toolResultRaw = ExtractToolUseResultRaw(rawJsonLine);

            // IMPORTANT: Use message.role if available, as it's more accurate than entry.type
            // In plan mode, some entries have type="user" but message.role="assistant"
            var effectiveRole = entry.message?.role ?? entry.type;

            switch (effectiveRole)
            {
                case "user":
                    // Handle tool_result blocks emitted as user messages
                    HandleToolResultBlocks(entry, conversation, toolUseIndex, timestamp, toolResultPayload, toolResultRaw);

                    if (entry.message != null)
                    {
                        var (content, attachments) = ExtractTextAndAttachments(entry.message.content);

                        // Check if this entry contains ONLY tool_result blocks (no actual user text)
                        // If so, skip creating a user message - the tool result content was already 
                        // handled by HandleToolResultBlocks and should not appear as a user message
                        var blocks = ParseContentBlocks(entry.message.content);
                        var hasOnlyToolResults = blocks != null && blocks.Length > 0 && 
                            Array.TrueForAll(blocks, b => b.type == "tool_result");
                        
                        // Fallback: If ParseContentBlocks failed (e.g. tool_result blocks don't deserialize
                        // to CliContentBlock), directly iterate JArray to check top-level block types
                        if (!hasOnlyToolResults && blocks == null && 
                            entry.message.content != null && entry.message.content.Type == JTokenType.Array)
                        {
                            var contentArray = entry.message.content as JArray;
                            
                            if (contentArray != null && contentArray.Count > 0)
                            {
                                // Check if ALL top-level blocks are tool_result type
                                hasOnlyToolResults = true;
                                foreach (var item in contentArray)
                                {
                                    var blockType = item["type"]?.ToString();
                                    if (blockType != "tool_result")
                                    {
                                        hasOnlyToolResults = false;
                                        break;
                                    }
                                }
                            }
                        }

                        if (hasOnlyToolResults)
                        {
                            // Skip - tool_result content was already processed
                            break;
                        }

                        if (string.IsNullOrEmpty(content))
                        {
                            content = ExtractContentFromJsonLine(rawJsonLine);
                        }

                        // Check if this is a local command output (e.g., /cost, /context)
                        // These should be displayed as assistant messages, not user messages
                        var isLocalCommandOutput = !string.IsNullOrEmpty(content) && 
                            (content.Contains("<local-command-stdout>") || content.Contains("<local-command-caveat>"));

                        List<IContextAttachment> contextAttachments = null;
                        if (!string.IsNullOrEmpty(content) &&
                            ContextAttachmentParser.TryExtractContext(content, out var cleaned, out var parsed))
                        {
                            content = cleaned;
                            if (parsed.Count > 0)
                            {
                                contextAttachments = parsed;
                            }
                        }
                        
                        // Clean local command tags (e.g., <local-command-stdout>, <local-command-caveat>)
                        if (!string.IsNullOrEmpty(content) &&
                            CommandTagParser.TryCleanLocalCommandTags(content, out var cleanedContent))
                        {
                            content = cleanedContent;
                        }
                        
                        if (!string.IsNullOrEmpty(content) &&
                            CommandTagParser.TryFormatCommandText(content, out var formattedContent))
                        {
                            content = formattedContent;
                        }

                        if (!string.IsNullOrEmpty(content) || attachments.Count > 0 || (contextAttachments?.Count ?? 0) > 0)
                        {
                            // Local command output should appear as assistant response
                            if (isLocalCommandOutput && !string.IsNullOrEmpty(content))
                            {
                                var assistantMsg = new Message
                                {
                                    Id = entry.uuid ?? Guid.NewGuid().ToString(),
                                    Role = MessageRole.Assistant,
                                    Content = content,
                                    Timestamp = timestamp ?? DateTime.Now,
                                    Attachments = attachments
                                };
                                conversation.Messages.Add(assistantMsg);
                            }
                            else
                            {
                                var userMsg = new Message
                                {
                                    Id = entry.uuid ?? Guid.NewGuid().ToString(),
                                    Role = MessageRole.User,
                                    Content = content,
                                    Timestamp = timestamp ?? DateTime.Now,
                                    Attachments = attachments,
                                    ContextAttachments = contextAttachments ?? new List<IContextAttachment>()
                                };
                                conversation.Messages.Add(userMsg);
                            }
                        }
                    }
                    break;

                case "assistant":
                    if (entry.message != null)
                    {
                        var blocks = ParseContentBlocks(entry.message.content);
                        if (blocks is { Length: > 0 })
                        {
                            // Calculate thinking duration if we have a pending thinking message
                            // and this is a non-thinking assistant entry
                            var hasThinkingBlock = Array.Exists(blocks, b => b.type == "thinking" || b.type == "redacted_thinking");
                            if (!hasThinkingBlock && pendingThinkingMessage != null && pendingThinkingTimestamp.HasValue && timestamp.HasValue)
                            {
                                var duration = (timestamp.Value - pendingThinkingTimestamp.Value).TotalSeconds;
                                if (duration > 0)
                                {
                                    pendingThinkingMessage.ThinkingDurationSeconds = duration;
                                }
                                pendingThinkingMessage = null;
                                pendingThinkingTimestamp = null;
                            }
                            
                            HandleAssistantContentBlocks(blocks, conversation, toolUseIndex, timestamp, rawJsonLine,
                                ref pendingThinkingMessage, ref pendingThinkingTimestamp);
                            break;
                        }

                        // Try string content fallback
                        var content = ExtractMessageContent(entry.message);
                        if (string.IsNullOrEmpty(content))
                        {
                            content = ExtractContentFromJsonLine(rawJsonLine);
                        }
                        
                        // Skip empty assistant messages (e.g., thinking-only blocks)
                        if (string.IsNullOrEmpty(content)) break;
                        
                        var assistantMsg = new Message
                        {
                            Id = entry.uuid ?? Guid.NewGuid().ToString(),
                            Role = MessageRole.Assistant,
                            Content = content,
                            Timestamp = timestamp ?? DateTime.Now
                        };

                        conversation.Messages.Add(assistantMsg);
                    }
                    break;
            }
        }

        private static void HandleAssistantContentBlocks(
            CliContentBlock[] blocks,
            Conversation conversation,
            Dictionary<string, ToolUse> toolUseIndex,
            DateTime? timestamp,
            string rawJsonLine,
            ref Message pendingThinkingMessage,
            ref DateTime? pendingThinkingTimestamp)
        {
            if (blocks == null) return;

            var textBuffer = new List<string>();
            var thinkingBuffer = new List<string>();
            var attachments = new List<ImageAttachment>();
            
            // Local state to track thinking - will be copied to ref params at end
            Message localPendingThinkingMessage = null;
            DateTime? localPendingThinkingTimestamp = null;

            foreach (var block in blocks)
            {
                switch (block.type)
                {
                    case "text":
                        if (!string.IsNullOrEmpty(block.text))
                        {
                            textBuffer.Add(block.text);
                        }
                        break;

                    case "thinking":
                        // Thinking blocks contain model's reasoning
                        if (!string.IsNullOrEmpty(block.thinking))
                        {
                            thinkingBuffer.Add(block.thinking);
                        }
                        break;

                    case "redacted_thinking":
                        // Redacted thinking - mark as present but no content visible
                        thinkingBuffer.Add("[Thinking redacted]");
                        break;

                    case "image":
                    case "input_image":
                    case "inline_image":
                    case "image_attachment":
                        var attachment = ParseImageAttachment(block);
                        if (attachment != null)
                        {
                            attachments.Add(attachment);
                        }
                        break;

                    case "tool_use":
                        FlushBuffers();

                        var toolUseId = block.id ?? Guid.NewGuid().ToString();
                        var toolUse = new ToolUse
                        {
                            Id = toolUseId,
                            Name = block.name,
                            Input = block.input,
                            FilePath = (block.name == "Write" || block.name == "Edit") ? ExtractFilePathFromInput(block.input) : null,
                            Status = ToolStatus.Running
                        };

                        toolUseIndex[toolUse.Id] = toolUse;

                        var toolMessage = new Message
                        {
                            Id = toolUse.Id,
                            Role = MessageRole.Tool,
                            Timestamp = timestamp ?? DateTime.Now
                        };
                        toolMessage.ToolUses.Add(toolUse);

                        conversation.Messages.Add(toolMessage);
                        break;

                    case "tool_result":
                        FlushBuffers();
                        ApplyToolResult(block, conversation, toolUseIndex, timestamp, ExtractToolUseResult(rawJsonLine), ExtractToolUseResultRaw(rawJsonLine));
                        break;
                }
            }

            FlushBuffers();
            
            // Update ref params with local state
            if (localPendingThinkingMessage != null)
            {
                pendingThinkingMessage = localPendingThinkingMessage;
                pendingThinkingTimestamp = localPendingThinkingTimestamp;
            }
            return;

            void FlushBuffers()
            {
                if (textBuffer.Count == 0 && attachments.Count == 0 && thinkingBuffer.Count == 0) return;

                var combined = string.Join("\n", textBuffer.Where(t => !string.IsNullOrEmpty(t)));
                var thinkingCombined = string.Join("\n", thinkingBuffer.Where(t => !string.IsNullOrEmpty(t)));
                textBuffer.Clear();
                thinkingBuffer.Clear();
                var attachmentCopy = attachments.Count > 0 ? new List<ImageAttachment>(attachments) : new List<ImageAttachment>();
                attachments.Clear();

                if (string.IsNullOrEmpty(combined) && attachmentCopy.Count == 0 && string.IsNullOrEmpty(thinkingCombined)) return;

                var assistantMsg = new Message
                {
                    Id = Guid.NewGuid().ToString(),
                    Role = MessageRole.Assistant,
                    Content = combined,
                    Timestamp = timestamp ?? DateTime.Now,
                    Attachments = attachmentCopy
                };

                // Add thinking content if present
                if (!string.IsNullOrEmpty(thinkingCombined))
                {
                    assistantMsg.ThinkingContent = thinkingCombined;
                    assistantMsg.IsThinkingExpanded = false; // Start collapsed for history
                    
                    // If this entry ONLY has thinking (no text), mark it as pending
                    // so we can calculate duration from the next entry's timestamp
                    if (string.IsNullOrEmpty(combined) && attachmentCopy.Count == 0)
                    {
                        localPendingThinkingMessage = assistantMsg;
                        localPendingThinkingTimestamp = timestamp;
                        // Duration will be set when the next non-thinking assistant entry arrives
                        assistantMsg.ThinkingDurationSeconds = null;
                    }
                    else
                    {
                        // Thinking and text in same entry - can't calculate duration from timestamps
                        assistantMsg.ThinkingDurationSeconds = null;
                    }
                }

                conversation.Messages.Add(assistantMsg);
            }
        }

        private static void HandleToolResultBlocks(
            CliSessionEntry entry,
            Conversation conversation,
            Dictionary<string, ToolUse> toolUseIndex,
            DateTime? timestamp,
            ToolUseResultPayload payload,
            JObject rawResult = null)
        {
            var blocks = ParseContentBlocks(entry.message?.content);
            if (blocks == null || blocks.Length == 0) return;

            foreach (var block in blocks)
            {
                if (block.type == "tool_result")
                {
                    ApplyToolResult(block, conversation, toolUseIndex, timestamp, payload, rawResult);
                }
            }
        }

        private static void ApplyToolResult(
            CliContentBlock block,
            Conversation conversation,
            Dictionary<string, ToolUse> toolUseIndex,
            DateTime? timestamp,
            ToolUseResultPayload payload,
            JObject rawResult = null)
        {
            var toolUseId = block.tool_use_id ?? block.id;
            if (string.IsNullOrEmpty(toolUseId)) return;

            if (!toolUseIndex.TryGetValue(toolUseId, out var toolUse))
            {
                toolUse = new ToolUse
                {
                    Id = toolUseId,
                    Name = block.name ?? payload?.name ?? "Tool",
                    Status = ToolStatus.Running
                };
                toolUseIndex[toolUseId] = toolUse;

                var toolMessage = new Message
                {
                    Id = toolUseId,
                    Role = MessageRole.Tool,
                    Timestamp = timestamp ?? DateTime.Now
                };
                toolMessage.ToolUses.Add(toolUse);
                conversation.Messages.Add(toolMessage);
            }

            if (!string.IsNullOrEmpty(block.content))
            {
                toolUse.Output = (toolUse.Output ?? "") + block.content;
            }

            if (payload != null)
            {
                if (!string.IsNullOrEmpty(payload.filePath))
                {
                    toolUse.FilePath = payload.filePath;
                }

                if (payload.structuredPatch is { Length: > 0 })
                {
                    toolUse.DiffContent = string.Join("\n", payload.structuredPatch);
                }
                else if (string.IsNullOrEmpty(toolUse.DiffContent) && !string.IsNullOrEmpty(payload.content))
                {
                    toolUse.Output = payload.content;
                }
            }

            if (string.IsNullOrEmpty(toolUse.Name) && !string.IsNullOrEmpty(block.name))
            {
                toolUse.Name = block.name;
            }

            toolUse.Status = block.is_error ? ToolStatus.Error : ToolStatus.Success;
            toolUse.IsStreaming = false;

            if (toolUse.Name == "AskUserQuestion" && rawResult?["answers"] is JObject flatAnswers)
            {
                var input = AskUserQuestionInput.FromJToken(toolUse.Input);
                var tracePayload = AskUserQuestionTraceFormatter
                    .BuildTraceAnswersPayloadFromClaudeFlat(input, flatAnswers);
                toolUse.Input = AskUserQuestionTraceFormatter.ApplyAnswers(toolUse.Input, tracePayload);
                toolUse.Status = AskUserQuestionTraceFormatter.IsCancelled(toolUse.Input)
                    ? ToolStatus.Error
                    : ToolStatus.Success;
            }
        }

        private static DateTime? ParseTimestamp(string timestamp)
        {
            if (!string.IsNullOrEmpty(timestamp) && DateTime.TryParse(timestamp, out var ts))
            {
                return ts;
            }

            return null;
        }

        private static string ExtractFilePathFromInput(JToken input)
        {
            if (input == null) return null;
            if (input.Type == JTokenType.Object && input["file_path"] != null)
            {
                return input["file_path"]?.ToString();
            }
            if (input.Type == JTokenType.String)
            {
                var match = Regex.Match(input.ToString(), "\"file_path\"\\s*:\\s*\"([^\"]+)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
            return null;
        }

        [Serializable]
        private class ToolUseResultEnvelope
        {
            public ToolUseResultPayload toolUseResult;
        }

        [Serializable]
        private class ToolUseResultPayload
        {
            public string type;
            public string filePath;
            public string content;
            public string[] structuredPatch;
            public string originalFile;
            public string name;
        }

        private static ToolUseResultPayload ExtractToolUseResult(string rawJsonLine)
        {
            if (string.IsNullOrEmpty(rawJsonLine)) return null;
            try
            {
                var envelope = JsonUtils.Deserialize<ToolUseResultEnvelope>(rawJsonLine);
                return envelope?.toolUseResult;
            }
            catch
            {
                return null;
            }
        }

        private static JObject ExtractToolUseResultRaw(string rawJsonLine)
        {
            if (string.IsNullOrEmpty(rawJsonLine)) return null;
            try
            {
                var parsed = JObject.Parse(rawJsonLine);
                return parsed["toolUseResult"] as JObject;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts text content from message content blocks or string content.
        /// Handles both formats: content as string (user messages) or content as block array (assistant).
        /// </summary>
        internal static string ExtractMessageContent(CliMessage message)
        {
            if (message == null) return "";

            var blocks = ParseContentBlocks(message.content);
            if (blocks != null && blocks.Length > 0)
            {
                var textParts = new List<string>();
                foreach (var block in blocks)
                {
                    if (block.type == "text" && !string.IsNullOrEmpty(block.text))
                    {
                        textParts.Add(block.text);
                    }
                }
                return string.Join("\n", textParts);
            }

            if (message.content is { Type: JTokenType.String })
            {
                return message.content.ToString();
            }

            return "";
        }

        /// <summary>
        /// Extracts combined text and any image attachments from a content token.
        /// </summary>
        private static (string text, List<ImageAttachment> attachments) ExtractTextAndAttachments(JToken contentToken)
        {
            var attachments = new List<ImageAttachment>();
            if (contentToken == null) return ("", attachments);

            var blocks = ParseContentBlocks(contentToken);
            if (blocks is { Length: > 0 })
            {
                var textParts = new List<string>();
                foreach (var block in blocks)
                {
                    if (block == null) continue;
                    if (string.Equals(block.type, "text", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(block.text))
                    {
                        textParts.Add(block.text);
                        continue;
                    }

                    if (IsImageBlock(block))
                    {
                        var attachment = ParseImageAttachment(block);
                        if (attachment != null)
                        {
                            attachments.Add(attachment);
                        }
                    }
                }

                return (string.Join("\n", textParts.Where(t => !string.IsNullOrEmpty(t))), attachments);
            }

            if (contentToken.Type == JTokenType.String)
            {
                return (contentToken.ToString(), attachments);
            }

            return ("", attachments);
        }

        /// <summary>
        /// Extracts message content directly from raw JSON line.
        /// Handles both string content and block array content formats.
        /// </summary>
        internal static string ExtractContentFromJsonLine(string jsonLine)
        {
            if (string.IsNullOrEmpty(jsonLine)) return "";

            // Find the "content": field
            var contentMatch = ContentFieldRegex.Match(jsonLine);
            if (!contentMatch.Success) return "";

            var afterContent = jsonLine[(contentMatch.Index + contentMatch.Length)..];
            
            // Check what follows "content":
            if (afterContent.Length == 0) return "";
            
            var firstChar = afterContent[0];
            
            // If it starts with a quote, it's a string value
            if (firstChar == '"')
            {
                var stringMatch = JsonStringRegex.Match(afterContent);
                if (stringMatch.Success)
                {
                    return UnescapeJsonString(stringMatch.Groups[1].Value);
                }
            }
            // If it starts with '[', it's a block array - use block regex
            else if (firstChar == '[')
            {
                var blockMatches = BlockTextRegex.Matches(afterContent);
                if (blockMatches.Count > 0)
                {
                    var textParts = new List<string>();
                    foreach (Match match in blockMatches)
                    {
                        var text = UnescapeJsonString(match.Groups[1].Value);
                        if (!string.IsNullOrEmpty(text))
                        {
                            textParts.Add(text);
                        }
                    }
                    return string.Join("\n", textParts);
                }
            }

            return "";
        }

        private static CliContentBlock[] ParseContentBlocks(JToken token)
        {
            if (token == null) return null;
            if (token.Type == JTokenType.Array)
            {
                try
                {
                    return token.ToObject<CliContentBlock[]>();
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        private static bool IsImageBlock(CliContentBlock block)
        {
            if (block == null) return false;

            var type = block.type?.ToLowerInvariant();
            if (type is "image" or "input_image" or "inline_image" or "image_attachment")
            {
                return true;
            }

            var mediaType = block.media_type ?? block.mime_type;

            if (string.IsNullOrEmpty(mediaType) && block.source is JObject sourceObj)
            {
                mediaType = sourceObj.Value<string>("media_type");
            }
            if (!string.IsNullOrEmpty(mediaType) && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static ImageAttachment ParseImageAttachment(CliContentBlock block)
        {
            if (block == null) return null;

            // Prefer explicit data field, then source.data, then content
            var data = block.data ??
                       (block.source is JObject sourceObj ? sourceObj.Value<string>("data") : null) ??
                       block.content;

            if (string.IsNullOrEmpty(data))
            {
                return null;
            }

            var mediaType = block.media_type ??
                            block.mime_type ??
                            (block.source is JObject mediaTypeSourceObj ? mediaTypeSourceObj.Value<string>("media_type") : null) ??
                            "image/png";

            return new ImageAttachment
            {
                Id = block.id ?? Guid.NewGuid().ToString(),
                MediaType = mediaType,
                Data = StripDataPrefix(data),
                FileName = block.file_name ?? block.name
            };
        }

        private static string StripDataPrefix(string data)
        {
            if (string.IsNullOrEmpty(data)) return data;
            const string marker = "base64,";
            var idx = data.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? data.Substring(idx + marker.Length) : data;
        }

        /// <summary>
        /// Unescapes common JSON string escape sequences.
        /// </summary>
        internal static string UnescapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            
            return s.Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\\"", "\"")
                    .Replace(@"\\", "\\");
        }

        /// <summary>
        /// Deduplicates sessions by SessionId, keeping the newest UpdatedAt.
        /// </summary>
        internal static List<CliSessionInfo> DeduplicateSessions(IEnumerable<CliSessionInfo> sessions)
        {
            var map = new Dictionary<string, CliSessionInfo>();

            foreach (var session in sessions)
            {
                if (session == null || string.IsNullOrEmpty(session.SessionId)) continue;

                if (!map.TryGetValue(session.SessionId, out var existing) || session.UpdatedAt > existing.UpdatedAt)
                {
                    map[session.SessionId] = session;
                }
            }

            return map.Values.OrderByDescending(s => s.UpdatedAt).ToList();
        }

        /// <summary>
        /// Extracts usage info from the last assistant message in a session file.
        /// Returns null if no usage info is found.
        /// </summary>
        public static ConversationUsageInfo GetSessionUsage(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            var storagePath = GetCliStoragePath();
            var filePath = Path.Combine(storagePath, $"{sessionId}.jsonl");

            if (!File.Exists(filePath))
                return null;

            try
            {
                // Read all lines and find the last assistant message with usage
                var lines = File.ReadAllLines(filePath);
                
                CliEntryWithUsage lastAssistantEntry = null;
                
                // Iterate in reverse to find last assistant message with usage
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var entry = JsonUtils.Deserialize<CliEntryWithUsage>(line);
                        if (entry?.type == "assistant" && entry.message?.usage != null)
                        {
                            lastAssistantEntry = entry;
                            break;
                        }
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }

                if (lastAssistantEntry?.message?.usage == null)
                    return null;

                var usage = lastAssistantEntry.message.usage;
                var modelName = lastAssistantEntry.message.model ?? "";

                return new ConversationUsageInfo
                {
                    InputTokens = usage.input_tokens + usage.cache_creation_input_tokens + usage.cache_read_input_tokens,
                    OutputTokens = usage.output_tokens,
                    ModelName = modelName,
                    ContextWindow = ModelContextWindows.GetContextWindow(modelName)
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ryx Sidekick] Failed to get session usage for {sessionId}: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Extracted usage data from CLI history entry.
    /// </summary>
    [Serializable]
    internal class CliUsageData
    {
        [Newtonsoft.Json.JsonProperty("input_tokens")] public int input_tokens;
        [Newtonsoft.Json.JsonProperty("output_tokens")] public int output_tokens;
        [Newtonsoft.Json.JsonProperty("cache_creation_input_tokens")] public int cache_creation_input_tokens;
        [Newtonsoft.Json.JsonProperty("cache_read_input_tokens")] public int cache_read_input_tokens;
    }

    /// <summary>
    /// Helper class to extract usage from history message.
    /// </summary>
    [Serializable]
    internal class CliMessageWithUsage
    {
        [Newtonsoft.Json.JsonProperty("model")] public string model;
        [Newtonsoft.Json.JsonProperty("usage")] public CliUsageData usage;
    }

    /// <summary>
    /// Entry wrapper for extracting usage.
    /// </summary>
    [Serializable]
    internal class CliEntryWithUsage
    {
        [Newtonsoft.Json.JsonProperty("type")] public string type;
        [Newtonsoft.Json.JsonProperty("message")] public CliMessageWithUsage message;
    }
}
