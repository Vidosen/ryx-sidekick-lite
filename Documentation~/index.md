# Ryx Sidekick documentation

Ryx Sidekick is a Unity Editor integration for the **Claude Code CLI**. It lets you chat with Claude inside Unity, attach project/scene context, and safely review edits.

## Requirements

- Unity **6.0+** (6000.x)
- Claude Code CLI installed (default command: `claude`)
- An authenticated Claude setup (Claude.ai, Console API key, or a supported third‑party provider)

## Open the window

- **Window → Ryx Sidekick** — opens the chat window

## Help & resources

- **Help → Ryx Sidekick → Documentation** — opens this documentation in your browser
- **Help → Ryx Sidekick → Changelog** — opens the changelog

## Quick start (recommended flow)

1. Open **Project Settings → Ryx Sidekick**.
2. Click **Validate CLI** (fix `CLI Path` if validation fails).
3. Open **Window → Ryx Sidekick** and log in if prompted.
4. Add context (files, selection, screenshots) and start chatting.

## Settings reference (Project Settings → Ryx Sidekick)

### Claude CLI Settings

- **CLI Path**: Executable name or absolute path (default: `claude`).
- **Working Directory**: Base directory for CLI execution and file operations (default: project root).
- **Model**: Default model ID passed to the CLI (e.g. `sonnet`, `opus`, `haiku`, or a custom model string).
- **Max Turns**: Maximum turns per request (forwarded to the CLI).
- **Permission Mode**: `default`, `plan`, or `bypassPermissions` (also switchable from the chat toolbar).
- **Verbose Logging**: Enables additional Unity console logs for debugging.

### Asset Refresh

- **Asset Refresh Mode**: Controls when `AssetDatabase.Refresh()` runs after edits (Off / After assistant completes / After edit+write tools / Manual).

### MCP (optional)

- **Enable MCP Config**: Generates/passes an MCP config file to Claude CLI.
- **Use Custom MCP Config**: Uses your own MCP config JSON instead of a generated one.
- **MCP Config Path**: Path to custom MCP config JSON (absolute or project-relative).
- **Generated MCP Config Path**: Where Ryx Sidekick writes the generated MCP config.
- **Unity MCP Server URL**: HTTP endpoint for MCP for Unity (default: `http://localhost:8080/mcp`).

### Providers / Debugging

- **Use Bedrock**: Routes Claude CLI through Bedrock (`CLAUDE_CODE_USE_BEDROCK=1`, requires AWS credentials).
- **Debug Mode**: Runs CLI in a visible OS terminal window (streaming output will not appear in Unity).

## Core features

### Streaming chat inside Unity

- Streams responses from the Claude CLI using `stream-json` (fast incremental updates).
- Renders assistant messages with Markdown (code blocks, links, etc.).
- Shows raw CLI output in the built-in **Terminal** tab for debugging.

### Conversation history (CLI-native)

Ryx Sidekick shows your past conversations by reading Claude CLI’s local history:

- Location: `~/.claude/projects/.../*.jsonl`
- Sessions are **read-only** from Unity; Ryx Sidekick does not write your history files.
- Long histories are loaded incrementally (pagination) for performance — see [Chat Optimization](ChatOptimization.md).

### Context attachments (better answers)

Ryx Sidekick supports two types of context:

- **File context**
  - Attach files from the Unity project.
  - Skips binary files, and truncates very large text files to keep payloads reasonable.
- **GameObject context**
  - Attach the current selection (scene objects or prefab assets).
  - Includes hierarchy path and component list so Claude understands your scene structure.

You can add context from:

- **Add Context** button (context menu)
- **Command Palette** (see below)

File context notes:

- Text-only (binary files are skipped).
- Max size: **100 KB**; large files are truncated (head + tail) with a truncation marker.

### Image attachments

Attach images to your message:

- Paste from clipboard (platform dependent)
- Drag & drop image files
- Capture **Scene View** and **Game View** screenshots

Images can be previewed in an overlay with zoom/pan controls.

### Command palette + slash commands

Ryx Sidekick includes a command palette inspired by VS Code:

- Built-in actions: attach file, attach selection, screenshots, open settings, new chat, model selection
- Auto-discovers Claude CLI slash commands and lets you insert or execute them

Tip: use the slash indicator near the input to open the palette, or type `/` to trigger slash suggestions.

### Useful input shortcuts

- **Enter**: send message (or stop a running turn).
- **Enter with modifiers** (Shift/Ctrl/Cmd/Alt): insert newline.
- **Tab**: cycle edit mode.
- **Ctrl/Cmd+V**: paste image from clipboard (when supported).

### Safe edits + permission UI

When Claude requests file edits or other controlled actions, Ryx Sidekick shows a permission overlay with context:

- **Allow** / **Deny**
- **Allow & Remember** (auto-accept that tool for the rest of the session)

Ryx Sidekick also tracks edits performed by tools and surfaces them in:

- **Files** tab (changed files list)
- **Diff** tab (per-file diff, with revert)

### Edit modes

You can choose how strict Ryx Sidekick is about edits:

- **Ask before edits** (`default`) — safest, prompts for each action
- **Plan mode** (`plan`) — encourages planning before edits
- **Edit automatically** (`bypassPermissions`) — fastest, least restrictive

Edit mode can be cycled from the input toolbar.

### Asset refresh control

After edits, Unity may need `AssetDatabase.Refresh()` to pick up file changes. Ryx Sidekick supports:

- Off
- After assistant completes
- After edit/write tools
- Manual (shows a banner to refresh on demand)

Configure in **Project Settings → Ryx Sidekick**.

### Models + Extended Thinking

- Pick a default model (Sonnet/Opus/Haiku) or enter a custom model ID.
- Optional **Extended Thinking** mode uses `--max-thinking-tokens` (configurable).

## Optional: MCP for Unity integration

Ryx Sidekick can integrate with **MCP for Unity** to enable deeper “tool-like” workflows for Claude:

- Generates or uses a custom MCP config JSON for the CLI

Setup is guided by the onboarding wizard, and configurable in **Project Settings → Ryx Sidekick → MCP**.

## Troubleshooting

### “Validate CLI” fails / CLI not found

- Ensure Claude Code CLI is installed and works in a terminal: `claude --version`
- In **Project Settings → Ryx Sidekick**, set `CLI Path` to an absolute path if needed.
- On macOS/Linux, Unity launched from a GUI may not inherit your shell PATH; using an absolute path is the most reliable fix.

### No streaming output in Unity

- Check **Project Settings → Ryx Sidekick → Debug Mode**.
  - When enabled, the CLI runs in a visible OS terminal window and streaming output does **not** appear in Unity.

### Conversations list is empty

- Verify the CLI has existing sessions for this project (the CLI stores sessions per project directory under `~/.claude/projects/`).
- Send a message once; a new session should appear after the CLI creates history on disk.

### Permission prompts feel too frequent

- Use **Allow & Remember** for tools you trust.
- Switch edit mode to **Plan mode** or **Edit automatically** depending on your workflow.

## Architecture documentation

For contributors and those interested in internals:

- [Chat Optimization](ChatOptimization.md) — pagination and windowing for long conversation histories

## Privacy & data notes

Ryx Sidekick is a UI and orchestration layer around the Claude Code CLI:

- It starts the local `claude` process and streams events over stdin/stdout.
- Messages and attachments you send are transmitted by the CLI to the configured Claude backend/provider.
- Conversation history shown in Unity is read from the CLI’s local storage.

Credential storage depends on platform and your authentication method:

- macOS: Keychain (with plaintext fallback).
- Windows/Linux: plaintext files in `~/.claude/` (same location used by the CLI).
