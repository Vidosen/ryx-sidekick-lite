// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Domain.Models
{
    /// <summary>
    /// Tracks tool_use blocks being streamed and accumulates their JSON input.
    /// Single responsibility: manage tool use state during streaming.
    /// </summary>
    internal class ToolUseTracker
    {
        private readonly Dictionary<string, ToolUse> _pendingToolUses = new();
        private readonly Func<bool> _verboseLogging;
        private PendingToolBlock _currentToolBlock;
        private readonly StringBuilder _currentResponse = new();

        public ToolUseTracker(Func<bool> verboseLogging = null)
        {
            _verboseLogging = verboseLogging ?? (static () => false);
        }

        public int CurrentResponseLength => _currentResponse.Length;

        public bool HasPendingToolUse(string toolUseId)
        {
            return !string.IsNullOrEmpty(toolUseId) && _pendingToolUses.ContainsKey(toolUseId);
        }

        public void RegisterToolUse(ToolUse toolUse)
        {
            if (!string.IsNullOrEmpty(toolUse?.Id))
            {
                _pendingToolUses[toolUse.Id] = toolUse;
            }
        }

        public void StartToolBlock(string toolUseId, string toolName, int blockIndex)
        {
            _currentToolBlock = new PendingToolBlock
            {
                ToolUseId = toolUseId,
                ToolName = toolName,
                BlockIndex = blockIndex
            };

            if (_verboseLogging())
            {
                Debug.Log($"[ToolUseTracker] Started tracking tool_use block: {toolName} ({toolUseId})");
            }
        }

        public void AppendToolInput(string partialJson)
        {
            _currentToolBlock?.InputJson.Append(partialJson);
        }

        public void AppendResponse(string text)
        {
            _currentResponse.Append(text);
        }

        /// <summary>
        /// Complete the current tool block and return a ToolUse if one was being tracked.
        /// Returns null if already handled or no block was active.
        /// </summary>
        public ToolUse CompleteToolBlock()
        {
            if (_currentToolBlock == null) return null;

            var toolUseId = _currentToolBlock.ToolUseId;
            var toolName = _currentToolBlock.ToolName;
            var input = _currentToolBlock.GetParsedInput();

            if (_verboseLogging())
            {
                Debug.Log($"[ToolUseTracker] Tool block completed: {toolName}");
            }

            // Skip if already handled via assistant_message
            if (HasPendingToolUse(toolUseId))
            {
                _currentToolBlock = null;
                return null;
            }

            var toolUse = new ToolUse
            {
                Id = toolUseId,
                Name = toolName,
                Input = input,
                Status = ToolStatus.Running
            };
            _pendingToolUses[toolUseId] = toolUse;

            _currentToolBlock = null;
            return toolUse;
        }

        /// <summary>
        /// Clear all tracked state for a new turn.
        /// </summary>
        public void Reset()
        {
            _currentToolBlock = null;
            _currentResponse.Clear();
            _pendingToolUses.Clear();
        }
    }
}
