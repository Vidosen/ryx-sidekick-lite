# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [Unreleased]

## [2.4.2] - 2026-06-22

### Changed

- Chat markdown now renders each paragraph (and heading, list item, table cell, quote) as a **single selectable text block** instead of splitting it into many pieces around inline code and file links. This fixes text selection breaking mid-paragraph, words wrapping incorrectly at formatting boundaries, and the large number of UI elements per message. Inline code keeps its rounded highlighted chip; detected asset-path links keep their type icon and single-click ping / double-click open.
- Redesigned the Pro paywall with a refined layout: a wide "engines" card that groups the extra providers as labelled chips, dedicated **Skills** and **MCP management** feature cards, crisp vector icons drawn in-editor (including the official Model Context Protocol mark), a gold accent hairline, and a clearer price block above the call-to-action.

## [2.4.1] - 2026-06-21

### Added

- In-editor one-click updates: when a newer version is published, the status-bar chip turns into **Update** and installs it in place without leaving Unity — free updates after a quick sign-in, Pro updates via your license. The "update available" notification's **Update** button now runs the same in-editor flow instead of opening the store page.

### Changed

- The chat timeline now renders with a virtualized `ListView`, removing the previous 50-message render cap — long conversations scroll smoothly from end to end. The assistant "typing" indicator now renders as the trailing row inside the timeline, below the last message, and scrolls together with the conversation instead of sitting in a fixed bar above the composer.
- When the assistant presents a plan for review, the chat timeline now scrolls to the **top** of the plan instead of leaving you at its end — so you start reading from the beginning, with the approval prompt and a jump-to-bottom button still available below.
- The "update available" notification is now anchored at the top of the conversation area instead of overlapping the composer and status bar.
- The installer compares versions semantically (major.minor.patch): it skips reinstalling an equal-or-newer version and applies only genuine upgrades.

### Fixed

- Context-window usage ("% used") is now accurate. The window size is no longer hard-coded to 200k: the real per-model window reported by the CLI (e.g. 1M for Opus) is captured from turn usage events, remembered per model, and reused when loading conversation history. The live usage figure now also counts cached input tokens (prompt-cache reads/writes), so long sessions no longer read as nearly empty.
- Empty and brand-new chats now show the branded welcome screen (logo + a rotating inspiring subtitle) instead of a bare "Conversation is empty." line over an empty list. The subtitle is picked at random from a curated set each time the welcome screen appears.

## [2.4.0] - 2026-06-20

### Added

- "MCP management" is now listed as a Pro feature so the paywall highlights live MCP introspection.
- Entitlement-aware paywall gate: owners who do not yet have the Pro package installed get a direct **Install Pro** action instead of a purchase prompt.

### Changed

- **MCP rework.** Unity MCP ("MCP for Unity" / Coplay) is no longer pre-configured or auto-connected by default. The previously seeded built-in MCP server entry is removed on upgrade, and Sidekick no longer auto-connects the bridge on window open. Add MCP servers manually under **Project Settings > Sidekick > MCP**; the status-bar MCP button now opens that page.
- Redesigned MCP settings page: a sectioned server registry, branded collapsible server cards, a Form↔JSON editor with IDE-style auto-indent, a backend-fed "Recommended for Unity" section, and a Lite Pro-upsell teaser for external servers that routes into the paywall.

### Fixed

- Readable gold paywall CTA and a compact, centred paywall modal.
- Pro invoice redemption now matches Pro by a stable `package_id` instead of the display name.

## [2.3.0] - 2026-06-18

### Added

- Firebase-backed delivery: signed-URL downloads that feed the existing two-stage installer, license/entitlement validation via RSA-signed tokens, and editor-driven self-update.
- Account login (OAuth) with an in-editor sign-in overlay, plus a web cabinet that redeems Asset Store invoices.
- Yearly editions with date-windowed download entitlements.

### Changed

- The update service now resolves the entitled release for the signed-in account.

## [2.2.0] - 2026-06-12

### Added

- Dynamic provider capability discovery: Claude models, slash commands, and account info now come from the CLI `initialize` handshake; Codex skills are introspected via `skills/list`.
- Skills surfaced as slash commands in the command palette, with a dedicated gold "Skills" section that doubles as a Pro entry point.
- Two-stage `.unitypackage` distribution installer (reconciler + payload) and a dev-side release exporter.

### Changed

- The model preset popup now stays open after picking a preset.

### Fixed

- Provider UI state is no longer clobbered by the registry fallback (per-provider persistence).

## [2.1.0] - 2026-06-08

### Added

- Pro paywall: a remote-config-driven offer with a Pro modal, locked provider rows in the selector, and a status-bar "Upgrade to Pro" chip — behind a remote kill-switch with a baked fallback config.
- In-editor update checker: compares installed Lite/Pro versions against a published `latest` and surfaces a dismissable update notification with changelog and store links.
- Marketing and documentation site (Astro + Starlight) with a published changelog and a Pro waitlist.

## [2.0.0] - 2026-06-07

### Changed

- **Breaking: Lite/Pro split.** The Cursor and Codex CLI providers have moved out of the base package into a new optional **Ryx Sidekick Pro** package (`com.ryxinteractive.sidekick.pro`). The base (Lite) package now ships **Claude** only — install Pro to restore Cursor and Codex.
- `SidekickSettings` now stores provider-specific values through a generic provider-keyed bag instead of shared snapshot fields.

### Added

- Per-provider settings as native Project Settings tree nodes with Lite/Pro ownership (Claude and Bedrock stay in Lite).
- MCP multi-server settings model (schema v2) with per-provider thinking and Bedrock options.

## [1.4.0] - 2026-05-20

### Changed

- Rebuilt the editor window on Unity App UI 2.x layered over UI Toolkit: a scoped App UI panel/theme bootstrap with dedicated popover, notification, and tooltip layers.
- Re-architected into a layered Presentation → Application → Domain structure with Infrastructure adapters; application logic no longer calls Unity Editor APIs directly, and assembly definitions enforce the dependency direction.
- Introduced MVVM ViewModels (composer, provider selector, permission overlay, AskUserQuestion, chat timeline) built on App UI `ObservableObject`/`RelayCommand`.
- Added an App UI Redux store for cross-cutting UI state (provider, turn, and permission slices) backed by pure reducers.
- Extracted message, per-tool, and attachment-chip rendering into factory and registry abstractions, fully encapsulating the markdown renderer.
- Migrated the attachment menu to an App UI Popover, the asset-refresh banner to an App UI Toast, and onboarding to an App UI Modal.

### Added

- Architecture-guard tests that enforce layer boundaries and keep provider-scoped types inside their provider folders.

## [1.3.2] - 2026-03-24

### Added

- Cursor event routing for real-time communication with persistent Cursor sessions.
- Onboarding wizard now includes a Codex provider selection card with CLI-based authentication guidance.
- Conditional MCP server start/stop methods in `McpForUnityController`, compiled only when the `HAS_UNITY_MCP` symbol is present.
- Provider-scoped runtime architecture (`SidekickArchitecture`, `SidekickWindowHost`, `PersistentConversationStorage`) for isolated per-provider state management.

### Changed

- Cursor output processing now uses batched pumping (16 lines per cycle) for improved streaming stability.

### Fixed

- Cursor tool mapping now correctly routes tool calls; related UI tests updated.

## [1.3.1] - 2026-03-12

### Added

- Codex launch overrides that map Sidekick MCP server settings into session-scoped `mcp_servers` config flags.
- Local plan implementation approval: after a Codex plan-mode turn completes, Sidekick prompts the user to approve or reject running the plan locally.

### Changed

- Codex sessions now advertise MCP support and restart the session when the effective MCP override set changes.
- Codex history reconstruction now tracks terminal sessions and properly extracts output from `write_stdin` tool calls; improved `request_user_input` round-tripping.
- Expanded Codex provider tests to cover MCP override building, session startup arguments, and restart behavior.

## [1.3.0] - 2026-03-08

### Added

- Multi-provider CLI support with selectable `Claude`, `Cursor`, and `Codex` runtimes in Project Settings.
- Provider-specific history readers and event parsers for Cursor and Codex, including improved Cursor thinking/content parsing.
- Session-based runtimes for Cursor and Codex using persistent connections to each CLI.
- Provider-aware collaboration and permission mode configuration, including explicit plan/permission mode separation.
- Compatibility adapters that normalize `AskUserQuestion` and `ExitPlanMode` flows across provider schemas.
- Inline `AskUserQuestion` traces for Codex sessions, including collapsible question details and recorded answers in the chat UI and restored history.

### Changed

- Settings and runtime orchestration now resolve CLI paths, history storage, prompt transport, and MCP injection per active provider.
- Permission overlays and control-request handling now work consistently across session-based providers and remembered permissions.
- Codex plan-mode streaming now turns incremental and completed plan items into `ExitPlanMode` traces, and dynamic `request_user_input` calls now round-trip through the session user-input flow.
- Codex history recovery now falls back to scanning session files when the index is incomplete and prefers the newest file for duplicate session ids.
- Selecting the `plan` permission preset now automatically aligns the collaboration mode to `plan`.
- Expanded automated coverage for Codex provider flows, provider mode selection, plan-mode rejection, and runtime/permission behavior.
- Hardened Codex, auth, and settings tests to isolate persisted CLI path and environment state between runs.

## [1.2.0] - 2026-02-28

### Added

- MCP onboarding now handles missing `MCP for Unity` package states and guides installation directly from Sidekick.
- MCP onboarding now surfaces server status with contextual start/stop actions during setup.

### Changed

- **AskUserQuestion auto-advance**: In multi-question dialogs, selecting an answer now automatically advances to the next unanswered question for faster navigation.
- Simplified the chat UI by removing the context panel and surfacing system-level failures directly in chat.
- Introduced centralized app and UI constants to keep menu paths, keys, asset references, and shared values consistent.
- Refined command palette styling, inline code rendering, and error presentation for a cleaner chat experience.
- Streamlined onboarding and login surfaces by removing obsolete terminal hints and tightening MCP setup UX.

### Fixed

- MCP setup and package detection behave more reliably when the package is absent or the server state changes during onboarding.

## [1.1.2] - 2026-01-27

### Added

- **MCP for Unity integration**: New status bar section showing MCP server connection status with Start/Stop button when `com.coplaydev.unity-mcp` package is installed.

### Fixed

- ExitPlanMode now renders directly in chat without nested ScrollView for better UX.
- Long chat histories no longer duplicate messages when scrolling up: message rendering is now windowed in the ScrollView (render-only; full conversation stays in memory).
- Auto-connect to already running MCP server on window open (configurable in settings).
- Optional auto-start MCP server feature (disabled by default).
- `McpConfigManager` now automatically uses MCP for Unity's URL when connected.

### Changed

- MCP section is hidden when Unity MCP package is not installed (uses conditional compilation via `versionDefines`).

## [1.1.1] - 2026-01-26

### Added

- **Persistent conversation restoration**: The last opened chat is now saved per-project and restored when reopening the Sidekick window, even if newer chats were created in VS Code or CLI.
- Automatic fallback to new chat if the saved conversation is stale (24+ hours without updates) or no longer available.

## [1.1.0] - 2026-01-26

### Added

- **@ Mentions for asset attachments**: Type `@` in the chat input to search and attach project assets (scripts, prefabs, text files) directly from the keyboard. The asset path is inserted as `@Assets/...` in the message text.
- Asset mention insertion for drag-and-drop, file browser, and selection-based attachment flows - the `@Assets/...` path is now automatically inserted at the caret position.
- `ProjectAssetSearchService` for fast asset search using Unity's `AssetDatabase.FindAssets` with fuzzy scoring.
- Dynamic overlay positioning for command palette and asset mentions based on input area height.

### Changed

- Command palette and asset mention overlays now reposition dynamically when the input area resizes (e.g., when context chips are added).

## [1.0.2] - 2026-01-14

### Added

- Auto-enable toolbar button on first install for improved discoverability.
- Changelog link in Help menu for quick access to version history.

### Changed

- Improved process management with synchronous cleanup and explicit stop flag for better state handling.
- Enhanced conversation switching to stop active turns before changing conversations.
- Refactored UI interactions to consistently check for active turns using `IsTurnInProgress`.
- Updated toolbar button label from "New Chat" to "Open Chat" for clarity.

### Fixed

- Toolbar button constructor parameter order for consistency.

## [1.0.1] - 2026-01-14

### Added

- Thinking UI with collapsible sections and chevron icons for assistant reasoning.
- Input field state persistence across domain reload and window close.
- System banner notification during Unity domain reload.
- Cursor-based pagination for optimized chat history loading.
- Collapsible input/output sections in tool call elements.
- Edit links for clickable diff file paths in tool responses.
- Auto-scroll to bottom during message streaming.
- Context usage tracking display in the UI.
- ExitPlanMode handling for plan mode control requests.
- Custom model name support in settings.
- Global typing indicator during assistant responses.

### Changed

- Updated Unity compatibility to 6.0.0+.
- Enhanced table rendering and styling in markdown.
- Improved login overlay UI and branding.
- Refactored MCP configuration (renamed McpBridgeManager to McpConfigManager).
- Improved command handling and message processing.

### Fixed

- Assistant message styling adjustments.
- Slash detection logic improvements.
- Model name handling corrections.

## [1.0.0] - 2026-01-08

### Added

- Sidekick editor window (**Window → Ryx Sidekick**) with streaming chat powered by AI coding CLIs.
- Project settings (**Project Settings → Ryx Sidekick**) including CLI validation and configuration.
- Authentication flows (Claude.ai OAuth, Console/API key, and third-party provider routing).
- Context attachments (project files + GameObjects) and image attachments (clipboard/drag&drop/screenshots).
- Command palette with built-in actions and auto-discovered CLI slash commands.
- Permission modal overlay, edit modes, and file change tracking with diff + revert.
- Optional MCP for Unity configuration support and onboarding wizard.
- Asset refresh modes and domain reload auto-resume.
