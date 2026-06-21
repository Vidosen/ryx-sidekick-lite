// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.UseCases.Attachments;
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEngine;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Handles control_request parsing and control_response formatting.
    /// Single responsibility: bidirectional control flow with CLI.
    /// </summary>
    internal class ControlRequestHandler
    {
        public event Action<PendingPermission> OnPermissionRequired;

        private const string DefaultRejectionMessage =
            "The user doesn't want to proceed with this tool use. " +
            "The tool use was rejected (eg. if it was a file edit, the new_string was NOT written to the file). " +
            "STOP what you are doing and wait for the user to tell you how to proceed.";

        private string _currentSessionId;

        public string CurrentSessionId
        {
            get => _currentSessionId;
            set => _currentSessionId = value;
        }

        /// <summary>
        /// Parse a control_request JSON and emit permission request if needed.
        /// Returns an error response JSON if the request type is unknown.
        /// </summary>
        public string HandleControlRequest(string json)
        {
            try
            {
                var jObj = JObject.Parse(json);
                var request = jObj["request"];
                var requestId = jObj["request_id"]?.Value<string>() ?? request?["request_id"]?.Value<string>();
                var subtype = request?["subtype"]?.Value<string>();
                var toolName = request?["tool_name"]?.Value<string>();

                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.Log($"[ControlRequestHandler] Control request: subtype={subtype}, requestId={requestId}, toolName={toolName}");
                }

                if (subtype == "can_use_tool")
                {
                    var permission = ExtractPermission(request, requestId);
                    OnPermissionRequired?.Invoke(permission);
                    return null; // No immediate response - UI will respond
                }

                // Unknown subtype - return error response
                if (!string.IsNullOrEmpty(requestId))
                {
                    return BuildErrorResponse(requestId, $"Unknown control request subtype: {subtype}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ControlRequestHandler] Failed to parse control_request: {ex.Message}");
                return null;
            }
        }

        private PendingPermission ExtractPermission(JToken request, string requestId)
        {
            var toolName = request?["tool_name"]?.Value<string>();
            var input = request?["input"];
            var toolUseId = request?["tool_use_id"]?.Value<string>();
            var agentId = request?["agent_id"]?.Value<string>();
            var suggestions = request?["permission_suggestions"];
            var blockedPath = request?["blocked_path"]?.Value<string>();
            var decisionReason = request?["decision_reason"]?.Value<string>();

            var filePath = input?["file_path"]?.Value<string>() ??
                           input?["filePath"]?.Value<string>();
            var command = input?["command"]?.Value<string>();

            return new PendingPermission
            {
                ToolUseId = toolUseId,
                ToolName = toolName,
                FilePath = filePath,
                Command = command,
                Input = input,
                SessionId = _currentSessionId,
                RawInput = input?.ToString(),
                RequestId = requestId,
                AgentId = agentId,
                Suggestions = suggestions,
                BlockedPath = blockedPath,
                DecisionReason = decisionReason,
                IsControlRequest = true,
                Kind = PendingPermissionKind.ClaudeControlRequest
            };
        }

        /// <summary>
        /// Build a control_response JSON for allowing or denying a tool.
        /// </summary>
        public static string BuildControlResponse(string requestId, string toolUseId, bool allow, JToken updatedInput = null, string message = null)
        {
            var innerResponse = new JObject
            {
                ["behavior"] = allow ? "allow" : "deny",
                ["toolUseID"] = toolUseId
            };

            if (allow)
            {
                innerResponse["updatedInput"] = updatedInput ?? new JObject();
            }
            else
            {
                innerResponse["message"] = message ?? DefaultRejectionMessage;
            }

            var response = new JObject
            {
                ["type"] = "control_response",
                ["response"] = new JObject
                {
                    ["subtype"] = "success",
                    ["request_id"] = requestId,
                    ["response"] = innerResponse
                }
            };

            return response.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Build a permission response JSON (legacy/simple format).
        /// </summary>
        public static string BuildPermissionResponseJson(bool allow, string toolUseId)
        {
            var content = allow ? "Approved by user" : "Denied by user";
            return BuildToolResultMessage(toolUseId, content, isError: !allow);
        }

        /// <summary>
        /// Build a generic tool_result message JSON for sending back to CLI via stdin.
        /// The message contains a single tool_result block with the given content.
        /// </summary>
        /// <param name="toolUseId">The tool_use.id to correlate with (from the original tool_use block).</param>
        /// <param name="content">The result content string (can be JSON-encoded if needed).</param>
        /// <param name="isError">If true, marks the tool_result as an error.</param>
        public static string BuildToolResultMessage(string toolUseId, string content, bool isError = false)
        {
            var toolId = string.IsNullOrEmpty(toolUseId) ? Guid.NewGuid().ToString("N") : toolUseId;

            var toolResultBlock = new JObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = toolId,
                ["content"] = content ?? ""
            };

            if (isError)
            {
                toolResultBlock["is_error"] = true;
            }

            var resultObj = new JObject
            {
                ["type"] = "user",
                ["message"] = new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray { toolResultBlock }
                }
            };

            return resultObj.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>
        /// Build a user message JSON for sending text plus optional image attachments and context attachments via stdin.
        /// </summary>
        public static string BuildUserMessage(
            string prompt,
            IReadOnlyList<ImageAttachment> attachments = null,
            IReadOnlyList<IContextAttachment> contextAttachments = null)
        {
            var content = new JArray();

            // Build text content with embedded context XML tags
            var textContent = new BuildPromptContextUseCase().Execute(prompt, contextAttachments);

            // Add text block if we have any text content
            if (textContent.Length > 0)
            {
                content.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = textContent
                });
            }

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    if (attachment == null) continue;

                    var base64 = NormalizeBase64Data(attachment.Data);
                    if (string.IsNullOrEmpty(base64)) continue;

                    var source = new JObject
                    {
                        ["type"] = "base64",
                        ["media_type"] = string.IsNullOrEmpty(attachment.MediaType) ? "image/png" : attachment.MediaType,
                        ["data"] = base64
                    };

                    var imageBlock = new JObject
                    {
                        ["type"] = "image",
                        ["source"] = source
                    };

                    content.Add(imageBlock);
                }
            }

            var userMessage = new JObject
            {
                ["type"] = "user",
                ["message"] = new JObject
                {
                    ["role"] = "user",
                    ["content"] = content
                }
            };

            return userMessage.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string NormalizeBase64Data(string data)
        {
            if (string.IsNullOrEmpty(data)) return data;

            // Allow passing a full data URL (data:image/png;base64,....) by stripping the prefix.
            if (!data.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return data;

            var commaIndex = data.IndexOf(',');
            return commaIndex >= 0 && commaIndex < data.Length - 1
                ? data.Substring(commaIndex + 1)
                : "";
        }

        private static string BuildErrorResponse(string requestId, string error)
        {
            var errorResponse = new JObject
            {
                ["type"] = "control_response",
                ["response"] = new JObject
                {
                    ["subtype"] = "error",
                    ["request_id"] = requestId,
                    ["error"] = error
                }
            };

            return errorResponse.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
