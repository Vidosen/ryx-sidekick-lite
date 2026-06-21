// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEngine;
using ILogger = Ryx.Sidekick.Editor.UseCases.Contracts.ILogger;

namespace Ryx.Sidekick.Editor
{
    internal sealed class PersistentConversationMetadata
    {
        public string ProviderId { get; set; }
        public string SessionId { get; set; }
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    internal sealed class PersistentConversationSnapshot
    {
        public string ProviderId { get; set; }
        public string SessionId { get; set; }
        public Conversation Conversation { get; set; }
        public ConversationUsageInfo Usage { get; set; }
        public bool ForcePersist { get; set; }
    }

    internal interface IPersistentConversationStore
    {
        void Save(PersistentConversationRecord record);
        PersistentConversationRecord Load(string providerId, string sessionId);
        PersistentConversationMetadata LoadMetadata(string providerId, string sessionId);
        List<PersistentConversationMetadata> LoadAllMetadata(string providerId);
        void Delete(string providerId, string sessionId);
        void Prune(string providerId, int maxConversationCount);
    }

    internal interface IPersistentConversationHistoryBackend
    {
        string ProviderId { get; }
        event Action<PersistentConversationSnapshot> ConversationSnapshotChanged;
        void PrimeConversationState(Conversation conversation, ConversationUsageInfo usage);
    }

    internal sealed class PersistentConversationStorageRepository : IConversationRepository
    {
        private readonly string _providerId;
        private readonly IPersistentConversationStore _store;
        private readonly IPersistentConversationHistoryBackend _historyBackend;

        public PersistentConversationStorageRepository(
            string providerId,
            IPersistentConversationStore store,
            IPersistentConversationHistoryBackend historyBackend = null)
        {
            _providerId = providerId;
            _store = store;
            _historyBackend = historyBackend;
        }

        public Task<List<CliSessionInfo>> ListSessionsAsync()
        {
            var metadata = _store?.LoadAllMetadata(_providerId) ?? new List<PersistentConversationMetadata>();
            var sessions = metadata
                .Where(item => !string.IsNullOrWhiteSpace(item.SessionId))
                .OrderByDescending(item => item.UpdatedAt)
                .Select(item => new CliSessionInfo
                {
                    SessionId = item.SessionId,
                    Title = string.IsNullOrWhiteSpace(item.Title) ? item.SessionId : item.Title,
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt,
                    FilePath = null
                })
                .ToList();
            return Task.FromResult(sessions);
        }

        public Task<Conversation> LoadConversationAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Task.FromResult<Conversation>(null);
            }

            var record = _store?.Load(_providerId, sessionId);
            if (record == null)
            {
                return Task.FromResult<Conversation>(null);
            }

            var conversation = record.ToConversation();
            _historyBackend?.PrimeConversationState(record.ToConversation(), record.ToUsage());
            return Task.FromResult(conversation);
        }

        public Task<ConversationUsageInfo> GetSessionUsageAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Task.FromResult<ConversationUsageInfo>(null);
            }

            var record = _store?.Load(_providerId, sessionId);
            return Task.FromResult(record?.ToUsage());
        }
    }

    internal sealed class PersistentConversationRecorder : IDisposable
    {
        private readonly IPersistentConversationHistoryBackend _backend;
        private readonly IPersistentConversationStore _store;
        private readonly ILogger _logger;
        private readonly int _maxConversationCount;
        private readonly TimeSpan _coalesceWindow;
        private readonly object _gate = new();
        private readonly Dictionary<string, PersistentConversationRecord> _pendingRecords = new(StringComparer.Ordinal);

        private bool _disposed;
        private bool _flushScheduled;

        public PersistentConversationRecorder(
            IPersistentConversationHistoryBackend backend,
            IPersistentConversationStore store,
            ILogger logger = null,
            int maxConversationCount = 50,
            TimeSpan? coalesceWindow = null)
        {
            _backend = backend;
            _store = store;
            _logger = logger;
            _maxConversationCount = Mathf.Max(1, maxConversationCount);
            _coalesceWindow = coalesceWindow ?? TimeSpan.FromMilliseconds(100);

            if (_backend != null)
            {
                _backend.ConversationSnapshotChanged += HandleConversationSnapshotChanged;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_backend != null)
            {
                _backend.ConversationSnapshotChanged -= HandleConversationSnapshotChanged;
            }

            FlushPending();
        }

        private void HandleConversationSnapshotChanged(PersistentConversationSnapshot snapshot)
        {
            if (_disposed || snapshot == null || string.IsNullOrWhiteSpace(snapshot.ProviderId) || string.IsNullOrWhiteSpace(snapshot.SessionId))
            {
                return;
            }

            var record = PersistentConversationRecord.FromSnapshot(snapshot);
            if (record == null)
            {
                return;
            }

            lock (_gate)
            {
                _pendingRecords[snapshot.SessionId] = record;
            }

            if (snapshot.ForcePersist)
            {
                FlushPending();
                return;
            }

            ScheduleFlush();
        }

        private void ScheduleFlush()
        {
            lock (_gate)
            {
                if (_flushScheduled || _disposed)
                {
                    return;
                }

                _flushScheduled = true;
            }

            _ = FlushPendingAsync();
        }

        private async Task FlushPendingAsync()
        {
            try
            {
                await Task.Delay(_coalesceWindow);
                FlushPending();
            }
            catch
            {
            }
        }

        private void FlushPending()
        {
            Dictionary<string, PersistentConversationRecord> records;
            lock (_gate)
            {
                if (_pendingRecords.Count == 0)
                {
                    _flushScheduled = false;
                    return;
                }

                records = new Dictionary<string, PersistentConversationRecord>(_pendingRecords, StringComparer.Ordinal);
                _pendingRecords.Clear();
                _flushScheduled = false;
            }

            foreach (var record in records.Values)
            {
                try
                {
                    _store.Save(record);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"Failed to save persistent conversation {record.SessionId}: {ex.Message}");
                }
            }

            try
            {
                var providerId = records.Values.FirstOrDefault()?.ProviderId;
                if (!string.IsNullOrWhiteSpace(providerId))
                {
                    _store.Prune(providerId, _maxConversationCount);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"Failed to prune persistent conversations: {ex.Message}");
            }
        }
    }

    internal sealed class PersistentConversationStore : IPersistentConversationStore
    {
        private readonly string _storageRootPath;

        public PersistentConversationStore(string storageRootPath = null)
        {
            _storageRootPath = string.IsNullOrWhiteSpace(storageRootPath)
                ? GetDefaultStorageRootPath()
                : storageRootPath;
        }

        public void Save(PersistentConversationRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.ProviderId) || string.IsNullOrWhiteSpace(record.SessionId))
            {
                return;
            }

            var filePath = GetConversationFilePath(record.ProviderId, record.SessionId);
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(filePath, JsonConvert.SerializeObject(record, Formatting.Indented));
        }

        public PersistentConversationRecord Load(string providerId, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var filePath = GetConversationFilePath(providerId, sessionId);
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                return DeserializeRecord(File.ReadAllText(filePath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ryx Sidekick] Failed to load persistent conversation {sessionId}: {ex.Message}");
                return null;
            }
        }

        public PersistentConversationMetadata LoadMetadata(string providerId, string sessionId)
        {
            var record = Load(providerId, sessionId);
            if (record == null)
            {
                return null;
            }

            return new PersistentConversationMetadata
            {
                ProviderId = record.ProviderId,
                SessionId = record.SessionId,
                Title = record.Title,
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt
            };
        }

        public List<PersistentConversationMetadata> LoadAllMetadata(string providerId)
        {
            var metadata = new List<PersistentConversationMetadata>();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return metadata;
            }

            var providerDirectory = GetProviderDirectory(providerId);
            if (!Directory.Exists(providerDirectory))
            {
                return metadata;
            }

            foreach (var filePath in Directory.GetFiles(providerDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var record = DeserializeRecord(File.ReadAllText(filePath));
                    if (record == null || string.IsNullOrWhiteSpace(record.SessionId))
                    {
                        continue;
                    }

                    metadata.Add(new PersistentConversationMetadata
                    {
                        ProviderId = record.ProviderId,
                        SessionId = record.SessionId,
                        Title = record.Title,
                        CreatedAt = record.CreatedAt,
                        UpdatedAt = record.UpdatedAt
                    });
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Ryx Sidekick] Skipping corrupted persistent conversation file {filePath}: {ex.Message}");
                }
            }

            return metadata
                .OrderByDescending(item => item.UpdatedAt)
                .ToList();
        }

        public void Delete(string providerId, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            var filePath = GetConversationFilePath(providerId, sessionId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        public void Prune(string providerId, int maxConversationCount)
        {
            if (string.IsNullOrWhiteSpace(providerId) || maxConversationCount < 1)
            {
                return;
            }

            var providerDirectory = GetProviderDirectory(providerId);
            if (!Directory.Exists(providerDirectory))
            {
                return;
            }

            var metadata = LoadAllMetadata(providerId);
            if (metadata.Count <= maxConversationCount)
            {
                return;
            }

            foreach (var staleSession in metadata.Skip(maxConversationCount))
            {
                Delete(providerId, staleSession.SessionId);
            }
        }

        private string GetProviderDirectory(string providerId)
        {
            return Path.Combine(_storageRootPath, providerId);
        }

        private string GetConversationFilePath(string providerId, string sessionId)
        {
            return Path.Combine(GetProviderDirectory(providerId), $"{sessionId}.json");
        }

        private static string GetDefaultStorageRootPath()
        {
            var projectRoot = Directory.GetCurrentDirectory();
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                projectRoot = Path.GetDirectoryName(Application.dataPath);
            }

            return Path.Combine(projectRoot, "Library", "Sidekick.Conversations");
        }

        private static PersistentConversationRecord DeserializeRecord(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var root = JObject.Parse(json);
            var version = root["Version"]?.Value<int>()
                ?? root["version"]?.Value<int>()
                ?? 0;
            if (version != PersistentConversationRecord.CurrentVersion)
            {
                return null;
            }

            return root.ToObject<PersistentConversationRecord>();
        }
    }

    [Serializable]
    internal sealed class PersistentConversationRecord
    {
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;
        public string ProviderId;
        public string SessionId;
        public string Title;
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
        public ConversationUsageInfo Usage = new();
        public List<PersistentConversationMessageRecord> Messages = new();

        public Conversation ToConversation()
        {
            var conversation = new Conversation
            {
                Id = SessionId,
                SessionId = SessionId,
                Title = string.IsNullOrWhiteSpace(Title) ? SessionId : Title,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                Messages = Messages?.SelectMany(message => message.ToMessages()).ToList() ?? new List<Message>()
            };
            return conversation;
        }

        public ConversationUsageInfo ToUsage()
        {
            return CloneValue(Usage) ?? new ConversationUsageInfo();
        }

        public static PersistentConversationRecord FromSnapshot(PersistentConversationSnapshot snapshot)
        {
            if (snapshot?.Conversation == null || string.IsNullOrWhiteSpace(snapshot.ProviderId) || string.IsNullOrWhiteSpace(snapshot.SessionId))
            {
                return null;
            }

            var conversation = snapshot.Conversation;
            return FromConversation(snapshot.ProviderId, snapshot.SessionId, conversation, snapshot.Usage);
        }

        public static PersistentConversationRecord FromConversation(
            string providerId,
            string sessionId,
            Conversation conversation,
            ConversationUsageInfo usage)
        {
            if (conversation == null || string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            return new PersistentConversationRecord
            {
                Version = CurrentVersion,
                ProviderId = providerId,
                SessionId = sessionId,
                Title = string.IsNullOrWhiteSpace(conversation.Title) ? sessionId : conversation.Title,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt,
                Usage = CloneValue(usage) ?? new ConversationUsageInfo(),
                Messages = conversation.Messages?.Select(PersistentConversationMessageRecord.FromMessage).ToList()
                    ?? new List<PersistentConversationMessageRecord>()
            };
        }

        private static T CloneValue<T>(T value)
        {
            if (value == null)
            {
                return default;
            }

            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        }
    }

    [Serializable]
    internal sealed class PersistentConversationMessageRecord
    {
        public string Id;
        public MessageRole Role;
        public DateTime Timestamp;
        public List<CodeBlock> CodeBlocks = new();
        public List<ImageAttachment> Attachments = new();
        public List<SerializedContextAttachment> ContextAttachments = new();
        public bool IsStreaming;
        public double? ThinkingDurationSeconds;
        public bool IsThinkingExpanded;
        public List<PersistentConversationBlockRecord> Blocks = new();

        public List<Message> ToMessages()
        {
            var message = new Message
            {
                Id = Id,
                Role = Role,
                Timestamp = Timestamp,
                CodeBlocks = CloneList(CodeBlocks),
                Attachments = CloneList(Attachments),
                ContextAttachments = DeserializeContextAttachments(ContextAttachments),
                IsStreaming = IsStreaming,
                ThinkingDurationSeconds = ThinkingDurationSeconds,
                IsThinkingExpanded = IsThinkingExpanded
            };

            if (Role == MessageRole.Tool)
            {
                message.ToolUses = Blocks
                    ?.Where(block => block != null && block.Type == PersistentConversationBlockType.ToolCall)
                    .Select(block => CloneValue(block.ToolUse))
                    .Where(toolUse => toolUse != null)
                    .ToList()
                    ?? new List<ToolUse>();

                return message.ToolUses.Count > 0
                    ? new List<Message> { message }
                    : new List<Message>();
            }

            var contentParts = new List<string>();
            var thinkingParts = new List<string>();
            var results = new List<Message>();

            if (Blocks != null)
            {
                for (var index = 0; index < Blocks.Count; index++)
                {
                    var block = Blocks[index];
                    if (block == null)
                    {
                        continue;
                    }

                    switch (block.Type)
                    {
                        case PersistentConversationBlockType.Prompt:
                        case PersistentConversationBlockType.Response:
                        case PersistentConversationBlockType.Error:
                            if (!string.IsNullOrEmpty(block.Content))
                            {
                                contentParts.Add(block.Content);
                            }
                            break;

                        case PersistentConversationBlockType.Thought:
                            if (!string.IsNullOrEmpty(block.Content))
                            {
                                thinkingParts.Add(block.Content);
                            }
                            break;

                        case PersistentConversationBlockType.ToolCall:
                            if (block.ToolUse != null)
                            {
                                results.Add(new Message
                                {
                                    Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : $"{Id}:tool:{index}",
                                    Role = MessageRole.Tool,
                                    Timestamp = Timestamp,
                                    ToolUses = new List<ToolUse> { CloneValue(block.ToolUse) }
                                });
                            }
                            break;
                    }
                }
            }

            message.Content = string.Concat(contentParts);
            message.ThinkingContent = thinkingParts.Count > 0 ? string.Concat(thinkingParts) : null;
            if (string.IsNullOrEmpty(message.Content) && !string.IsNullOrEmpty(message.ThinkingContent))
            {
                message.Content = message.ThinkingContent;
            }

            if (!string.IsNullOrEmpty(message.Content)
                || !string.IsNullOrEmpty(message.ThinkingContent)
                || (message.Attachments?.Count ?? 0) > 0
                || (message.ContextAttachments?.Count ?? 0) > 0)
            {
                results.Insert(0, message);
            }

            return results;
        }

        public static PersistentConversationMessageRecord FromMessage(Message message)
        {
            if (message == null)
            {
                return null;
            }

            return new PersistentConversationMessageRecord
            {
                Id = message.Id,
                Role = message.Role,
                Timestamp = message.Timestamp,
                CodeBlocks = CloneList(message.CodeBlocks),
                Attachments = CloneList(message.Attachments),
                ContextAttachments = SerializeContextAttachments(message.ContextAttachments),
                IsStreaming = message.IsStreaming,
                ThinkingDurationSeconds = message.ThinkingDurationSeconds,
                IsThinkingExpanded = message.IsThinkingExpanded,
                Blocks = PersistentConversationBlockRecord.FromMessage(message)
            };
        }

        private static List<SerializedContextAttachment> SerializeContextAttachments(IReadOnlyList<IContextAttachment> attachments)
        {
            if (attachments == null || attachments.Count == 0)
            {
                return new List<SerializedContextAttachment>();
            }

            var serialized = new List<SerializedContextAttachment>();
            foreach (var attachment in attachments)
            {
                if (attachment == null)
                {
                    continue;
                }

                switch (attachment)
                {
                    case FileContextAttachment file:
                        serialized.Add(new SerializedContextAttachment
                        {
                            AttachmentType = "File",
                            JsonData = JsonConvert.SerializeObject(file, ContextAttachmentJson.SerializerSettings)
                        });
                        break;
                    case GameObjectContextAttachment gameObject:
                        serialized.Add(new SerializedContextAttachment
                        {
                            AttachmentType = "GameObject",
                            JsonData = JsonConvert.SerializeObject(gameObject, ContextAttachmentJson.SerializerSettings)
                        });
                        break;
                    case ScreenshotContextAttachment screenshot:
                        serialized.Add(new SerializedContextAttachment
                        {
                            AttachmentType = "Screenshot",
                            JsonData = JsonConvert.SerializeObject(screenshot, ContextAttachmentJson.SerializerSettings)
                        });
                        break;
                }
            }

            return serialized;
        }

        private static List<IContextAttachment> DeserializeContextAttachments(List<SerializedContextAttachment> serializedAttachments)
        {
            var attachments = new List<IContextAttachment>();
            if (serializedAttachments == null || serializedAttachments.Count == 0)
            {
                return attachments;
            }

            foreach (var serializedAttachment in serializedAttachments)
            {
                if (serializedAttachment == null || string.IsNullOrWhiteSpace(serializedAttachment.JsonData))
                {
                    continue;
                }

                try
                {
                    var attachment = serializedAttachment.AttachmentType switch
                    {
                        "File" => (IContextAttachment)JsonConvert.DeserializeObject<FileContextAttachment>(serializedAttachment.JsonData),
                        "GameObject" => JsonConvert.DeserializeObject<GameObjectContextAttachment>(serializedAttachment.JsonData),
                        "Screenshot" => JsonConvert.DeserializeObject<ScreenshotContextAttachment>(serializedAttachment.JsonData),
                        _ => null
                    };

                    if (attachment != null)
                    {
                        attachments.Add(attachment);
                    }
                }
                catch
                {
                }
            }

            return attachments;
        }

        private static List<T> CloneList<T>(List<T> source)
        {
            if (source == null || source.Count == 0)
            {
                return new List<T>();
            }

            return JsonConvert.DeserializeObject<List<T>>(JsonConvert.SerializeObject(source)) ?? new List<T>();
        }

        private static T CloneValue<T>(T value)
        {
            if (value == null)
            {
                return default;
            }

            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        }
    }

    internal enum PersistentConversationBlockType
    {
        Prompt,
        Response,
        Thought,
        ToolCall,
        Plan,
        Error
    }

    [Serializable]
    internal sealed class PersistentConversationBlockRecord
    {
        public PersistentConversationBlockType Type;
        public string Content;
        public bool IsComplete = true;
        public ToolUse ToolUse;
        public string JsonPayload;

        public static List<PersistentConversationBlockRecord> FromMessage(Message message)
        {
            var blocks = new List<PersistentConversationBlockRecord>();
            if (message == null)
            {
                return blocks;
            }

            switch (message.Role)
            {
                case MessageRole.User:
                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        blocks.Add(new PersistentConversationBlockRecord
                        {
                            Type = PersistentConversationBlockType.Prompt,
                            Content = message.Content,
                            IsComplete = !message.IsStreaming
                        });
                    }
                    break;

                case MessageRole.Assistant:
                    if (!string.IsNullOrEmpty(message.ThinkingContent))
                    {
                        blocks.Add(new PersistentConversationBlockRecord
                        {
                            Type = PersistentConversationBlockType.Thought,
                            Content = message.ThinkingContent,
                            IsComplete = !message.IsStreaming
                        });
                    }

                    if (!string.IsNullOrEmpty(message.Content)
                        && (!message.IsThinkingBlock || !string.Equals(message.Content, message.ThinkingContent, StringComparison.Ordinal)))
                    {
                        blocks.Add(new PersistentConversationBlockRecord
                        {
                            Type = PersistentConversationBlockType.Response,
                            Content = message.Content,
                            IsComplete = !message.IsStreaming
                        });
                    }
                    else if (message.IsThinkingBlock && !string.IsNullOrEmpty(message.Content) && blocks.Count == 0)
                    {
                        blocks.Add(new PersistentConversationBlockRecord
                        {
                            Type = PersistentConversationBlockType.Thought,
                            Content = message.Content,
                            IsComplete = !message.IsStreaming
                        });
                    }
                    break;

                case MessageRole.Tool:
                    if (message.ToolUses != null)
                    {
                        foreach (var toolUse in message.ToolUses.Where(toolUse => toolUse != null))
                        {
                            blocks.Add(new PersistentConversationBlockRecord
                            {
                                Type = PersistentConversationBlockType.ToolCall,
                                ToolUse = CloneValue(toolUse),
                                IsComplete = !toolUse.IsStreaming
                            });
                        }
                    }
                    break;

                case MessageRole.System:
                    if (!string.IsNullOrEmpty(message.Content))
                    {
                        blocks.Add(new PersistentConversationBlockRecord
                        {
                            Type = message.Content.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase)
                                ? PersistentConversationBlockType.Error
                                : PersistentConversationBlockType.Response,
                            Content = message.Content,
                            IsComplete = !message.IsStreaming
                        });
                    }
                    break;
            }

            return blocks;
        }

        private static T CloneValue<T>(T value)
        {
            if (value == null)
            {
                return default;
            }

            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
        }
    }
}
