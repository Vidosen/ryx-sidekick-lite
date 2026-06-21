// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Commands;
using Ryx.Sidekick.Editor.Domain.Parsing;
using Ryx.Sidekick.Editor.Presentation.Controllers;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.Shell.CommandPalette;
using Ryx.Sidekick.Editor.Presentation.Shell.CommandPalette.Providers;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Commands;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    internal sealed class CommandPalettePresenter : IDisposable
    {
        private readonly ComposerContextAttachmentPresenter _contextAttachmentPresenter;
        private readonly ProviderSelectorViewModel _providerSelectorViewModel;
        private readonly AttachmentController _attachmentController;
        private readonly Action _createNewConversation;
        private readonly CommandRegistry _commandRegistry;
        private readonly BuiltInActionsProvider _builtInActionsProvider;
        private readonly SlashCommandsProvider _slashCommandsProvider;
        private readonly McpPromptsProvider _mcpPromptsProvider;

        private SidekickWindowView _view;
        private CommandPaletteController _commandPaletteController;
        private AssetMentionController _assetMentionController;
        private ChatController _chatController;
        private Label _slashIndicator;
        private CancellationTokenSource _bindCts;
        private bool _disposed;

        public CommandPalettePresenter(
            ComposerContextAttachmentPresenter contextAttachmentPresenter,
            ProviderSelectorViewModel providerSelectorViewModel,
            AttachmentController attachmentController,
            Action createNewConversation,
            IProPresence proPresence = null,
            Action openPaywallForSkills = null)
        {
            _contextAttachmentPresenter = contextAttachmentPresenter;
            _providerSelectorViewModel = providerSelectorViewModel;
            _attachmentController = attachmentController;
            _createNewConversation = createNewConversation;

            _commandRegistry = new CommandRegistry();

            _builtInActionsProvider = new BuiltInActionsProvider
            {
                OnAttachFile = () => _contextAttachmentPresenter?.BrowseProjectFiles(),
                OnAttachSelection = () => _contextAttachmentPresenter?.AddSelectionAsContext(),
                OnAttachSceneScreenshot = () => _contextAttachmentPresenter?.CaptureSceneViewScreenshot(),
                OnAttachGameScreenshot = () => _contextAttachmentPresenter?.CaptureGameViewScreenshot(),
                OnOpenSettings = () => SettingsService.OpenProjectSettings("Project/Sidekick"),
                OnOpenHelp = () => Application.OpenURL("https://github.com/anthropics/claude-code"),
                OnNewChat = _createNewConversation,
                OnSelectModel = model => _providerSelectorViewModel?.SelectModelPresetCommand.Execute(model),
                GetCurrentModel = () => SidekickSettings.instance.Model,
                GetAvailableModels = () => _providerSelectorViewModel?.ModelPresets?.Select(model => model.Name)
                    ?? Enumerable.Empty<string>()
            };

            _slashCommandsProvider = new SlashCommandsProvider
            {
                OnExecuteSlashCommand = ExecuteSlashCommand,
                OnInsertSlashCommand = InsertSlashCommand,
                SkillsLocked = proPresence?.IsInstalled != true,
                OnLockedSkillActivated = () => openPaywallForSkills?.Invoke()
            };

            _mcpPromptsProvider = new McpPromptsProvider
            {
                OnExecutePrompt = ExecuteMcpPrompt
            };

            _commandRegistry.RegisterProvider(_builtInActionsProvider);
            _commandRegistry.RegisterProvider(_slashCommandsProvider);
            _commandRegistry.RegisterProvider(_mcpPromptsProvider);
        }

        public bool IsOpen => _commandPaletteController?.IsOpen == true || _assetMentionController?.IsOpen == true;

        public bool IsCommandPaletteOpen => _commandPaletteController?.IsOpen == true;

        public void BindView(SidekickWindowView view)
        {
            _view = view;
            var chatPanel = _view?.Root?.Q<VisualElement>("chat-panel");
            if (chatPanel != null && _commandPaletteController == null)
            {
                _commandPaletteController = new CommandPaletteController(
                    _commandRegistry,
                    _view.InputField,
                    chatPanel);

                _commandPaletteController.OnSlashCommandExecute += ExecuteSlashCommand;
            }
            else if (_commandPaletteController == null)
            {
                Debug.LogWarning($"[CommandPalette] Cannot create controller - chatPanel: {chatPanel != null}, registry: {_commandRegistry != null}");
            }

            if (chatPanel != null && _attachmentController != null && _assetMentionController == null)
            {
                _assetMentionController = new AssetMentionController(
                    _view.InputField,
                    chatPanel,
                    _attachmentController);
            }

            RegisterViewHandlers();
        }

        public void RebindProviderScope(ChatController chatController, IProviderSlashCommandSource slashCommandSource)
        {
            _chatController = chatController;

            // Cancel any in-flight load from the previous bind.
            _bindCts?.Cancel();
            _bindCts?.Dispose();
            _bindCts = null;

            if (slashCommandSource == null)
            {
                _slashCommandsProvider?.SetSlashCommands(Array.Empty<SlashCommand>());
                _commandRegistry?.Invalidate();
                return;
            }

            var cts = new CancellationTokenSource();
            _bindCts = cts;
            var token = cts.Token;

            // Fire-and-forget: load commands on a background task, marshal result to main thread.
            _ = LoadCommandsAsync(slashCommandSource, token);
        }

        private async Task LoadCommandsAsync(
            IProviderSlashCommandSource source,
            CancellationToken token)
        {
            try
            {
                var commands = await source.LoadCommandsAsync(token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                EditorApplication.delayCall += () =>
                {
                    if (_disposed || token.IsCancellationRequested)
                    {
                        return;
                    }

                    _slashCommandsProvider?.SetSlashCommands(commands);
                    _commandRegistry?.Invalidate();
                };
            }
            catch (OperationCanceledException)
            {
                // Cancelled — ignore.
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CommandPalette] Failed to load slash commands: {ex.Message}");
                EditorApplication.delayCall += () =>
                {
                    if (_disposed || token.IsCancellationRequested)
                    {
                        return;
                    }

                    _slashCommandsProvider?.SetSlashCommands(Array.Empty<SlashCommand>());
                    _commandRegistry?.Invalidate();
                };
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _bindCts?.Cancel();
            _bindCts?.Dispose();
            _bindCts = null;

            UnregisterViewHandlers();

            if (_commandPaletteController != null)
            {
                _commandPaletteController.OnSlashCommandExecute -= ExecuteSlashCommand;
                _commandPaletteController.Dispose();
                _commandPaletteController = null;
            }

            _assetMentionController?.Dispose();
            _assetMentionController = null;

            _view = null;
            _chatController = null;
        }

        private void RegisterViewHandlers()
        {
            _slashIndicator = _view?.Root?.Q<Label>(className: "sk-slash-indicator");
            _slashIndicator?.RegisterCallback<ClickEvent>(HandleSlashIndicatorClicked);
            _view?.InputField?.RegisterCallback<ChangeEvent<string>>(HandleInputChanged);
            _view?.Root?.RegisterCallback<KeyDownEvent>(HandleRootKeyDown, TrickleDown.TrickleDown);
            _view?.Root?.RegisterCallback<ClickEvent>(HandleRootClicked);
        }

        private void UnregisterViewHandlers()
        {
            _slashIndicator?.UnregisterCallback<ClickEvent>(HandleSlashIndicatorClicked);
            _view?.InputField?.UnregisterCallback<ChangeEvent<string>>(HandleInputChanged);
            _view?.Root?.UnregisterCallback<KeyDownEvent>(HandleRootKeyDown, TrickleDown.TrickleDown);
            _view?.Root?.UnregisterCallback<ClickEvent>(HandleRootClicked);
            _slashIndicator = null;
        }

        private void HandleSlashIndicatorClicked(ClickEvent evt)
        {
            if (_assetMentionController?.IsOpen == true)
            {
                _assetMentionController.Close();
            }

            if (_commandPaletteController?.IsOpen == true)
            {
                _commandPaletteController.Close();
            }
            else
            {
                _commandPaletteController?.OpenGeneral();
            }
        }

        private void HandleInputChanged(ChangeEvent<string> evt)
        {
            var newValue = evt.newValue;
            _view.InputField.schedule.Execute(() =>
            {
                if (_disposed)
                {
                    return;
                }

                var caretIndex = _view.InputField?.cursorIndex ?? newValue.Length;
                var wasSlashOpen = _commandPaletteController?.IsOpen == true;
                var wasAtOpen = _assetMentionController?.IsOpen == true;

                _commandPaletteController?.UpdateFromInput(newValue, caretIndex);
                _assetMentionController?.UpdateFromInput(newValue, caretIndex);

                if (!wasSlashOpen && _commandPaletteController?.IsOpen == true && _assetMentionController?.IsOpen == true)
                {
                    _assetMentionController.Close();
                }
                else if (!wasAtOpen && _assetMentionController?.IsOpen == true && _commandPaletteController?.IsOpen == true)
                {
                    _commandPaletteController.Close();
                }
            });
        }

        private void HandleRootKeyDown(KeyDownEvent evt)
        {
            var slashOpen = _commandPaletteController?.IsOpen == true;
            var atOpen = _assetMentionController?.IsOpen == true;

            if (!slashOpen && !atOpen)
            {
                return;
            }

            var isPaletteKey = evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter
                or KeyCode.UpArrow or KeyCode.DownArrow
                or KeyCode.Tab or KeyCode.Escape;

            if (!isPaletteKey)
            {
                return;
            }

            evt.StopPropagation();
            evt.StopImmediatePropagation();

            if (slashOpen)
            {
                _commandPaletteController.HandleKeyDown(evt);
            }
            else if (atOpen)
            {
                _assetMentionController.HandleKeyDown(evt);
            }
        }

        private void HandleRootClicked(ClickEvent evt)
        {
            if (_commandPaletteController?.IsOpen == true && !IsClickInsideCommandPalette(evt, _slashIndicator))
            {
                _commandPaletteController.Close();
            }

            if (_assetMentionController?.IsOpen == true && !IsClickInsideAssetMention(evt))
            {
                _assetMentionController.Close();
            }
        }

        private bool IsClickInsideCommandPalette(ClickEvent evt, VisualElement slashIndicator)
        {
            if (_commandPaletteController?.View == null)
            {
                return false;
            }

            var target = evt.target as VisualElement;
            while (target != null)
            {
                if (target == _commandPaletteController.View) return true;
                if (target == slashIndicator) return true;
                if (target == _view?.InputField) return true;
                target = target.parent;
            }

            return false;
        }

        private bool IsClickInsideAssetMention(ClickEvent evt)
        {
            if (_assetMentionController?.View == null)
            {
                return false;
            }

            var target = evt.target as VisualElement;
            while (target != null)
            {
                if (target == _assetMentionController.View) return true;
                if (target == _view?.InputField) return true;
                target = target.parent;
            }

            return false;
        }

        private void ExecuteSlashCommand(string commandText)
        {
            if (string.IsNullOrEmpty(commandText))
            {
                return;
            }

            if (commandText == "/screenshot-scene" || commandText == "screenshot-scene")
            {
                _contextAttachmentPresenter?.CaptureSceneViewScreenshot();
                return;
            }

            if (commandText == "/screenshot-game" || commandText == "screenshot-game")
            {
                _contextAttachmentPresenter?.CaptureGameViewScreenshot();
                return;
            }

            _chatController?.SendMessage(commandText, null, null);
        }

        private void InsertSlashCommand(string commandText)
        {
            if (_view?.InputField == null || string.IsNullOrEmpty(commandText))
            {
                return;
            }

            var (newText, newCaret) = SlashTextTransform.InsertCommand(
                _view.InputField.value,
                _view.InputField.cursorIndex,
                commandText);

            _view.InputField.value = newText;
            _view.InputField.cursorIndex = newCaret;
            _view.InputField.selectIndex = newCaret;
            _view.InputField.Focus();
        }

        private void ExecuteMcpPrompt(string promptName)
        {
            if (string.IsNullOrEmpty(promptName))
            {
                return;
            }

            _chatController?.SendMessage(promptName, null, null);
        }
    }
}
