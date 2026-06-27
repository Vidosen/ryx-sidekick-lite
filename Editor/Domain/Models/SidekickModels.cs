// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Domain.Models
{
    /// <summary>
    /// Represents a conversation session with Claude.
    /// </summary>
    [Serializable]
    internal class Conversation
    {
        public string Id;
        public string Title;
        public string SessionId;
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
        public List<Message> Messages = new();

        #region History State (Runtime only, not serialized)

        /// <summary>
        /// Full path to the JSONL history file for this conversation.
        /// Set when loading from CLI storage.
        /// </summary>
        [NonSerialized] public string HistoryFilePath;

        /// <summary>
        /// True while an async load operation is in progress.
        /// Prevents duplicate loads when scrolling.
        /// </summary>
        [NonSerialized] public bool IsHistoryLoading;
        
        #endregion

        public Conversation()
        {
            Id = Guid.NewGuid().ToString();
            CreatedAt = DateTime.Now;
            UpdatedAt = DateTime.Now;
            Title = "New Chat";
        }
    }

    internal enum ConversationListLoadState
    {
        Idle,
        Initializing,
        Loading,
        Ready,
        Empty,
        Error
    }

    internal enum ConversationHistoryLoadState
    {
        Idle,
        Initializing,
        Loading,
        Ready,
        Empty,
        Error
    }

    internal sealed class ConversationLoadStatus<TState>
        where TState : struct, Enum
    {
        public ConversationLoadStatus(TState state, string message = null, bool canRetry = false)
        {
            State = state;
            Message = message;
            CanRetry = canRetry;
        }

        public TState State { get; }
        public string Message { get; }
        public bool CanRetry { get; }
    }

    /// <summary>
    /// Represents a single message in a conversation.
    /// </summary>
    [Serializable]
    internal class Message
    {
        public string Id;
        public MessageRole Role;
        public string Content;
        public DateTime Timestamp;
        public List<ToolUse> ToolUses = new();
        public List<CodeBlock> CodeBlocks = new();
        public List<ImageAttachment> Attachments = new();
        public List<IContextAttachment> ContextAttachments = new();
        public bool IsStreaming;

        /// <summary>
        /// Extended thinking content (separate from main response text).
        /// </summary>
        public string ThinkingContent;

        /// <summary>
        /// Duration of thinking in seconds (if known, e.g. from live stream timing).
        /// Null if duration is unknown (e.g. loaded from history).
        /// </summary>
        public double? ThinkingDurationSeconds;

        /// <summary>
        /// Whether the thinking block is currently expanded in the UI.
        /// </summary>
        public bool IsThinkingExpanded;

        /// <summary>
        /// True if this message is a thinking block (not main response text).
        /// Used to render thinking as separate inline element in chronological order.
        /// </summary>
        public bool IsThinkingBlock;

        public Message()
        {
            Id = Guid.NewGuid().ToString();
            Timestamp = DateTime.Now;
        }
    }

    internal enum MessageRole
    {
        User,
        Assistant,
        System,
        Tool  // For inline tool call/result blocks
    }

    internal enum ToolKind
    {
        Unknown,
        Read,
        Write,
        Edit,
        Move,
        Bash,
        Search,
        ListDirectory,
        Todo,
        AskUserQuestion,
        ImplementPlan,
        ExitPlanMode,
        WebFetch,
        WebSearch,
        Delete,
        Mcp
    }

    internal enum TerminalInputEventKind
    {
        Input,
        Interrupt
    }

    [Serializable]
    internal class TerminalInputEvent
    {
        public TerminalInputEventKind Kind;
        public string RawChars;
        public string DisplayText;
    }

    /// <summary>
    /// Represents a tool use/call by Claude, rendered as an inline chat block.
    /// </summary>
    [Serializable]
    internal class ToolUse
    {
        public string Id;
        public string ProviderId;
        public ToolKind Kind;
        public string Name;
        public string RawName;
        public string RawTitle;
        public string DecisionKey;
        public JToken Input;
        public string Output;
        public ToolStatus Status;
        public bool IsStreaming;      // True while output is still arriving
        public bool IsCollapsed;      // UI state for collapsible content
        public string FilePath;       // For file-related tools (Read, Edit, Write)
        public string DiffContent;    // For Edit tool - unified diff
        public string CommandLine;    // For Bash tool
        public string TerminalSessionId;
        public List<TerminalInputEvent> TerminalInputEvents = new();
        public string Description;    // Human-readable description of what the tool is doing (e.g. for Bash)
        public bool IsOutputExpanded; // UI state for expandable output panel
        public bool IsIoExpanded;     // UI state for the collapsible Input/Output panel (model-backed so it survives ListView recycling)
    }

    internal enum ToolStatus
    {
        Pending,
        Running,
        Success,
        Error
    }

    /// <summary>
    /// Represents a code block in a message.
    /// </summary>
    [Serializable]
    internal class CodeBlock
    {
        public string Language;
        public string Code;
    }

    /// <summary>
    /// Type of context attachment.
    /// </summary>
    internal enum AttachmentType
    {
        Image,
        File,
        GameObject,
        Screenshot
    }

    /// <summary>
    /// Kind of screenshot (Scene View or Game View).
    /// </summary>
    internal enum ScreenshotKind
    {
        SceneView,
        GameView
    }

    /// <summary>
    /// Base interface for context attachments (files, GameObjects).
    /// </summary>
    internal interface IContextAttachment
    {
        string Id { get; }
        AttachmentType Type { get; }
        string DisplayName { get; }
        string ToContextXml();
    }

    internal static class ContextAttachmentJson
    {
        // Unity.Mathematics structs (float3/quaternion) expose swizzle properties that return
        // the same type, which Newtonsoft's reflection-based serializer flags as self-reference.
        // Ignoring the loop is safe here because IContextAttachment graphs have no real cycles.
        internal static readonly JsonSerializerSettings SerializerSettings = new()
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
    }

    /// <summary>
    /// Represents an image attachment sent alongside a message.
    /// </summary>
    [Serializable]
    internal class ImageAttachment
    {
        public string Id;
        public string MediaType;
        public string Data;
        public string FileName;

        /// <summary>
        /// If set, this image is linked to a ScreenshotContextAttachment and they should be removed together.
        /// </summary>
        public string LinkedContextAttachmentId;
    }

    /// <summary>
    /// Represents a file from the project as context attachment.
    /// </summary>
    [Serializable]
    internal class FileContextAttachment : IContextAttachment
    {
        public string Id { get; set; }
        public AttachmentType Type => AttachmentType.File;
        public string DisplayName => Path.GetFileName(FilePath);

        public string FilePath;      // Relative path: "Assets/Scripts/Player.cs"
        public string Content;       // File contents (may be truncated)
        public bool IsTruncated;     // True if content was truncated
        public long OriginalSize;    // Original file size in bytes

        public string ToContextXml()
        {
            var escapedContent = System.Security.SecurityElement.Escape(Content ?? "");
            return $"<context_file path=\"{FilePath}\">{escapedContent}</context_file>";
        }
    }

    /// <summary>
    /// Represents a GameObject from scene or prefab as context attachment.
    /// </summary>
    [Serializable]
    internal class GameObjectContextAttachment : IContextAttachment
    {
        public string Id { get; set; }
        public AttachmentType Type => AttachmentType.GameObject;
        public string DisplayName => ObjectName;

        public string ObjectName;           // "Player" or "Main Camera"
        public string ScenePath;            // "Assets/Scenes/Main.unity" or null for prefabs
        public string HierarchyPath;        // "Canvas/Panel/Button"
        public int InstanceId;              // Runtime instance ID
        public List<string> ComponentNames; // ["Transform", "Rigidbody", "PlayerController"]
        public bool IsPrefab;               // True if this is a prefab asset
        public string PrefabPath;           // For prefabs: "Assets/Prefabs/Player.prefab"

        public string ToContextXml()
        {
            var sb = new StringBuilder();
            sb.Append("<context_gameobject");

            if (!string.IsNullOrEmpty(ScenePath))
                sb.Append($" scene=\"{ScenePath}\"");

            sb.Append($" path=\"{HierarchyPath}\"");
            sb.Append($" instance_id=\"{InstanceId}\"");

            if (IsPrefab && !string.IsNullOrEmpty(PrefabPath))
                sb.Append($" prefab=\"{PrefabPath}\"");

            sb.Append(">");

            var components = ComponentNames != null ? string.Join(", ", ComponentNames) : "";
            sb.Append(components);
            sb.Append("</context_gameobject>");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents a screenshot from Scene View or Game View as context attachment.
    /// Contains metadata about the view and links to an ImageAttachment for the actual image data.
    /// </summary>
    [Serializable]
    internal class ScreenshotContextAttachment : IContextAttachment
    {
        public string Id { get; set; }
        public AttachmentType Type => AttachmentType.Screenshot;
        public string DisplayName => Kind == ScreenshotKind.SceneView ? "Scene View" : "Game View";

        public ScreenshotKind Kind;
        public int Width;
        public int Height;
        public DateTime Timestamp;

        /// <summary>ID of the linked ImageAttachment containing the actual screenshot data.</summary>
        public string LinkedImageAttachmentId;

        // Scene View camera metadata (only populated for SceneView screenshots)
        public float3 CameraPosition;
        public quaternion CameraRotation;
        public bool IsOrthographic;
        public float FieldOfView;       // For perspective cameras
        public float OrthographicSize;  // For orthographic cameras
        public float NearClipPlane;
        public float FarClipPlane;

        public string ToContextXml()
        {
            var sb = new StringBuilder();
            sb.Append("<context_screenshot");
            sb.Append($" kind=\"{(Kind == ScreenshotKind.SceneView ? "scene" : "game")}\"");
            sb.Append($" width=\"{Width}\"");
            sb.Append($" height=\"{Height}\"");
            sb.Append($" timestamp=\"{Timestamp:O}\"");

            if (Kind == ScreenshotKind.SceneView)
            {
                sb.Append($" camera_pos=\"{CameraPosition.x:F2},{CameraPosition.y:F2},{CameraPosition.z:F2}\"");
                var euler = ((Quaternion)CameraRotation).eulerAngles;
                sb.Append($" camera_rot=\"{euler.x:F1},{euler.y:F1},{euler.z:F1}\"");
                sb.Append($" projection=\"{(IsOrthographic ? "orthographic" : "perspective")}\"");
                if (IsOrthographic)
                    sb.Append($" ortho_size=\"{OrthographicSize:F2}\"");
                else
                    sb.Append($" fov=\"{FieldOfView:F1}\"");
                sb.Append($" near=\"{NearClipPlane:F2}\"");
                sb.Append($" far=\"{FarClipPlane:F1}\"");
            }

            sb.Append(" />");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Represents a file change from Claude.
    /// </summary>
    [Serializable]
    internal class FileChange
    {
        public string FilePath;
        public FileChangeType Type;
        public string OriginalContent;
        public string NewContent;
        public List<DiffLine> DiffLines = new();
        public bool IsApplied;
    }

    internal enum FileChangeType
    {
        Added,
        Modified,
        Deleted
    }

    /// <summary>
    /// Represents a single line in a diff.
    /// </summary>
    [Serializable]
    internal class DiffLine
    {
        public DiffLineType Type;
        public string Content;
        public int? OldLineNumber;
        public int? NewLineNumber;
    }

    internal enum DiffLineType
    {
        Context,
        Added,
        Removed
    }

    // ============================================
    // Claude CLI JSON Response Models
    // Based on --output-format stream-json
    // ============================================

    /// <summary>
    /// Base class for all CLI stream events.
    /// </summary>
    [Serializable]
    internal class StreamEvent
    {
        [JsonProperty("type")] public string type;
    }

    /// <summary>
    /// System event from CLI.
    /// </summary>
    [Serializable]
    internal class SystemEvent : StreamEvent
    {
        [JsonProperty("subtype")] public string subtype;
        [JsonProperty("message")] public string message;
        [JsonProperty("session_id")] public string session_id;
        [JsonProperty("status")] public string status;
        [JsonProperty("error")] public string error;
        [JsonProperty("error_status")] public int error_status;
        [JsonProperty("attempt")] public int attempt;
        [JsonProperty("max_retries")] public int max_retries;
        [JsonProperty("apiKeySource")] public string apiKeySource;
    }

    /// <summary>
    /// Assistant message event.
    /// </summary>
    [Serializable]
    internal class AssistantMessageEvent : StreamEvent
    {
        [JsonProperty("message")] public AssistantMessage message;
    }

    [Serializable]
    internal class AssistantMessage
    {
        [JsonProperty("id")] public string id;
        [JsonProperty("type")] public string type;
        [JsonProperty("role")] public string role;
        [JsonProperty("content")] public ContentBlock[] content;
        [JsonProperty("model")] public string model;
        [JsonProperty("stop_reason")] public string stop_reason;
        [JsonProperty("usage")] public Usage usage;
    }

    [Serializable]
    internal class ContentBlock
    {
        [JsonProperty("type")] public string type;
        [JsonProperty("text")] public string text;
        [JsonProperty("id")] public string id;
        [JsonProperty("name")] public string name;
        [JsonProperty("input")] public JToken input;
        [JsonProperty("source")] public JToken source;
        [JsonProperty("data")] public string data;
        [JsonProperty("media_type")] public string media_type;
        [JsonProperty("mime_type")] public string mime_type;
        [JsonProperty("file_name")] public string file_name;
        /// <summary>Thinking text for thinking content blocks.</summary>
        [JsonProperty("thinking")] public string thinking;
        /// <summary>Signature for thinking blocks (integrity verification).</summary>
        [JsonProperty("signature")] public string signature;
    }

    [Serializable]
    internal class Usage
    {
        [JsonProperty("input_tokens")] public int input_tokens;
        [JsonProperty("output_tokens")] public int output_tokens;
        [JsonProperty("cache_creation_input_tokens")] public int cache_creation_input_tokens;
        [JsonProperty("cache_read_input_tokens")] public int cache_read_input_tokens;
    }

    /// <summary>
    /// Content block delta event for streaming.
    /// </summary>
    [Serializable]
    internal class ContentBlockDeltaEvent : StreamEvent
    {
        [JsonProperty("index")] public int index;
        [JsonProperty("delta")] public Delta delta;
    }

    [Serializable]
    internal class Delta
    {
        [JsonProperty("type")] public string type;
        [JsonProperty("text")] public string text;
        [JsonProperty("partial_json")] public string partial_json;
        /// <summary>Thinking text chunk for thinking_delta events.</summary>
        [JsonProperty("thinking")] public string thinking;
        /// <summary>Signature chunk for signature_delta events (streaming signature for thinking block).</summary>
        [JsonProperty("signature")] public string signature;
    }

    /// <summary>
    /// Tool use result event.
    /// </summary>
    [Serializable]
    internal class ToolResultEvent : StreamEvent
    {
        [JsonProperty("tool_use_id")] public string tool_use_id;
        [JsonProperty("content")] public string content;
        [JsonProperty("is_error")] public bool is_error;
    }

    /// <summary>
    /// Result event when CLI completes.
    /// </summary>
    [Serializable]
    internal class ResultEvent : StreamEvent
    {
        [JsonProperty("subtype")] public string subtype;
        [JsonProperty("cost_usd")] public int cost_usd;
        [JsonProperty("total_cost_usd")] public float total_cost_usd;
        [JsonProperty("is_error")] public bool is_error;
        [JsonProperty("duration_ms")] public int duration_ms;
        [JsonProperty("duration_api_ms")] public int duration_api_ms;
        [JsonProperty("num_turns")] public int num_turns;
        [JsonProperty("result")] public string result;
        [JsonProperty("session_id")] public string session_id;
        [JsonProperty("modelUsage")] public Dictionary<string, ModelUsageInfo> modelUsage;
    }

    /// <summary>
    /// Model usage information including context window size.
    /// </summary>
    [Serializable]
    internal class ModelUsageInfo
    {
        [JsonProperty("contextWindow")] public int contextWindow;
    }

    /// <summary>
    /// Represents extracted usage info from a conversation history.
    /// </summary>
    [Serializable]
    internal class ConversationUsageInfo
    {
        public int InputTokens;
        public int OutputTokens;
        public int TotalTokens => InputTokens + OutputTokens;
        public string ModelName;
        public int ContextWindow;
    }

    // ============================================
    // CLI JSONL Session Storage Models
    // For reading ~/.claude/projects/{project}/*.jsonl
    // ============================================

    /// <summary>
    /// Permission request event - emitted when CLI needs user confirmation for a tool action.
    /// </summary>
    [Serializable]
    internal class PermissionRequestEvent : StreamEvent
    {
        /// <summary>The tool_use_id correlating to the original tool invocation.</summary>
        [JsonProperty("tool_use_id")] public string ToolUseId;
        
        /// <summary>Name of the tool requesting permission (e.g., Write, Edit, Bash).</summary>
        [JsonProperty("tool_name")] public string ToolName;
        
        /// <summary>File path for file-related tools (Write, Edit, Read).</summary>
        [JsonProperty("file_path")] public string FilePath;
        
        /// <summary>Human-readable message describing the permission request.</summary>
        [JsonProperty("message")] public string Message;
        
        /// <summary>The full input payload from the original tool_use (for executing if allowed).</summary>
        [JsonProperty("input")] public JToken Input;
        
        /// <summary>Session ID for the request.</summary>
        [JsonProperty("session_id")] public string SessionId;
        
        /// <summary>UUID for the request event.</summary>
        [JsonProperty("uuid")] public string Uuid;
    }

    // ============================================
    // Control Request/Response Models
    // For bidirectional stream-json communication with CLI
    // Based on VS Code extension reference
    // ============================================

    /// <summary>
    /// Control request from CLI - used for permission prompts (can_use_tool), hooks, etc.
    /// The CLI waits for a control_response before proceeding.
    /// </summary>
    [Serializable]
    internal class ControlRequestEvent : StreamEvent
    {
        [JsonProperty("request")] public ControlRequest Request;
        [JsonProperty("request_id")] public string RequestId;
    }

    /// <summary>
    /// Inner request payload of a control_request.
    /// </summary>
    [Serializable]
    internal class ControlRequest
    {
        /// <summary>Subtype of control request: "can_use_tool", "hook_callback", "mcp_message".</summary>
        [JsonProperty("subtype")] public string Subtype;
        
        /// <summary>Tool name being requested (for can_use_tool).</summary>
        [JsonProperty("tool_name")] public string ToolName;
        
        /// <summary>Tool input parameters (for can_use_tool).</summary>
        [JsonProperty("input")] public JToken Input;
        
        /// <summary>Permission suggestions from CLI (e.g., auto-accept hints).</summary>
        [JsonProperty("permission_suggestions")] public JToken PermissionSuggestions;
        
        /// <summary>Blocked path that triggered the request.</summary>
        [JsonProperty("blocked_path")] public string BlockedPath;
        
        /// <summary>Reason for the permission decision prompt.</summary>
        [JsonProperty("decision_reason")] public string DecisionReason;
        
        /// <summary>Tool use ID for correlation.</summary>
        [JsonProperty("tool_use_id")] public string ToolUseId;
        
        /// <summary>Agent ID that initiated the tool call.</summary>
        [JsonProperty("agent_id")] public string AgentId;
    }

    /// <summary>
    /// Control response sent back to CLI stdin.
    /// </summary>
    [Serializable]
    internal class ControlResponse
    {
        [JsonProperty("type")] public string Type = "control_response";
        [JsonProperty("response")] public ControlResponsePayload Response;
    }

    /// <summary>
    /// Payload for control_response.
    /// </summary>
    [Serializable]
    internal class ControlResponsePayload
    {
        /// <summary>Subtype: "allow", "deny", "error".</summary>
        [JsonProperty("subtype")] public string Subtype;
        
        /// <summary>The request_id from the original control_request.</summary>
        [JsonProperty("request_id")] public string RequestId;
        
        /// <summary>Tool use ID for correlation.</summary>
        [JsonProperty("toolUseID")] public string ToolUseId;
        
        /// <summary>Whether the tool is allowed to proceed.</summary>
        [JsonProperty("allow")] public bool Allow;
        
        /// <summary>Message to send back to the assistant on rejection.</summary>
        [JsonProperty("message")] public string Message;
        
        /// <summary>Error message (for error subtype).</summary>
        [JsonProperty("error")] public string Error;
    }

    /// <summary>
    /// Represents a single line/entry in a CLI session JSONL file.
    /// </summary>
    [Serializable]
    internal class CliSessionEntry
    {
        [JsonProperty("type")] public string type;           // "user", "assistant", "queue-operation", etc.
        [JsonProperty("sessionId")] public string sessionId;
        [JsonProperty("uuid")] public string uuid;
        [JsonProperty("parentUuid")] public string parentUuid;
        [JsonProperty("timestamp")] public string timestamp;
        [JsonProperty("cwd")] public string cwd;
        [JsonProperty("version")] public string version;
        [JsonProperty("gitBranch")] public string gitBranch;
        [JsonProperty("message")] public CliMessage message;
    }

    /// <summary>
    /// Message content in CLI session entry.
    /// Both user and assistant messages use content blocks array.
    /// </summary>
    [Serializable]
    internal class CliMessage
    {
        [JsonProperty("role")] public string role;           // "user" or "assistant"
        [JsonProperty("model")] public string model;          // Model used for assistant messages
        [JsonProperty("id")] public string id;             // Message ID
        // Content can be a string (user) or an array of blocks (assistant)
        [JsonProperty("content")] public JToken content;  // Raw content token
    }

    /// <summary>
    /// Content block in assistant messages.
    /// </summary>
    [Serializable]
    internal class CliContentBlock
    {
        [JsonProperty("type")] public string type;           // "text", "tool_use", "thinking", "redacted_thinking"
        [JsonProperty("text")] public string text;           // For text blocks
        [JsonProperty("id")] public string id;             // For tool_use blocks
        [JsonProperty("name")] public string name;           // Tool name
        [JsonProperty("input")] public JToken input;          // Tool input (JSON)
        [JsonProperty("tool_use_id")] public string tool_use_id;    // For tool_result blocks
        [JsonProperty("content")] public string content;        // For tool_result output
        [JsonProperty("is_error")] public bool is_error;         // For tool_result error flag
        [JsonProperty("source")] public JToken source;           // For image blocks
        [JsonProperty("data")] public string data;               // Base64 inline data for images
        [JsonProperty("media_type")] public string media_type;   // Mime type for images
        [JsonProperty("mime_type")] public string mime_type;     // Alternate mime field
        [JsonProperty("file_name")] public string file_name;     // Optional file name
        [JsonProperty("thinking")] public string thinking;       // For thinking blocks
        [JsonProperty("signature")] public string signature;     // For thinking blocks (integrity)
    }

    #region AskUserQuestion Models

    /// <summary>
    /// Input structure for AskUserQuestion tool.
    /// </summary>
    [Serializable]
    internal class AskUserQuestionInput
    {
        [JsonProperty("questions")]
        public List<AskUserQuestionItem> Questions = new();

        public static AskUserQuestionInput FromJToken(JToken input)
        {
            if (input == null) return null;
            try
            {
                return input.ToObject<AskUserQuestionInput>();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A single question in the AskUserQuestion tool.
    /// </summary>
    [Serializable]
    internal class AskUserQuestionItem
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("header")]
        public string Header;

        [JsonProperty("question")]
        public string Question;

        [JsonProperty("options")]
        public List<AskUserQuestionOption> Options = new();

        [JsonProperty("multiSelect")]
        public bool MultiSelect;

        [JsonProperty("isOther")]
        public bool IsOther;

        [JsonProperty("isSecret")]
        public bool IsSecret;

        [JsonProperty("otherPlaceholder")]
        public string OtherPlaceholder;
    }

    /// <summary>
    /// An option for a question.
    /// </summary>
    [Serializable]
    internal class AskUserQuestionOption
    {
        [JsonProperty("label")]
        public string Label;

        [JsonProperty("description")]
        public string Description;
    }

    /// <summary>
    /// Result structure for AskUserQuestion tool response.
    /// </summary>
    [Serializable]
    internal class AskUserQuestionResult
    {
        [JsonProperty("answers")]
        public List<AskUserQuestionAnswer> Answers = new();

        [JsonProperty("selectedLabelsByHeader")]
        public Dictionary<string, List<string>> SelectedLabelsByHeader = new();

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.None);
        }
    }

    /// <summary>
    /// A single answer in the result.
    /// </summary>
    [Serializable]
    internal class AskUserQuestionAnswer
    {
        [JsonProperty("index")]
        public int Index;

        [JsonProperty("header")]
        public string Header;

        [JsonProperty("question")]
        public string Question;

        [JsonProperty("selected")]
        public List<AskUserQuestionSelectedOption> Selected = new();
    }

    /// <summary>
    /// A selected option in an answer.
    /// </summary>
    [Serializable]
    internal class AskUserQuestionSelectedOption
    {
        [JsonProperty("label")]
        public string Label;

        [JsonProperty("description")]
        public string Description;

        [JsonProperty("otherText")]
        public string OtherText;
    }

    #endregion

    #region Input Field Persistence

    /// <summary>
    /// State of the input field for persistence across domain reload and window close.
    /// </summary>
    [Serializable]
    internal class InputFieldState
    {
        public string InputText;
        public List<SerializedContextAttachment> ContextAttachments = new();
        public List<ImageAttachment> ImageAttachments = new();
    }

    /// <summary>
    /// Wrapper for polymorphic serialization of IContextAttachment.
    /// </summary>
    [Serializable]
    internal class SerializedContextAttachment
    {
        public string AttachmentType; // "File", "GameObject", "Screenshot"
        public string JsonData;       // Serialized attachment data
    }

    #endregion
}




