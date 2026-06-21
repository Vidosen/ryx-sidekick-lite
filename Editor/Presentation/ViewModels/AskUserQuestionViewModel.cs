// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Domain.Questions;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Questions;
using Unity.AppUI.MVVM;
using Debug = UnityEngine.Debug;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    [ObservableObject]
    internal partial class AskUserQuestionViewModel : IDisposable
    {
        private IRuntimeOrchestrator _runtimeOrchestrator;
        private readonly ISettingsStore _settingsStore;
        /// <summary>
        /// Re-wired per provider scope via <see cref="SetComposerViewModel"/>.
        /// May be null when no provider scope is active.
        /// </summary>
        private ComposerViewModel _composerViewModel;
        private Action<string> _submitLocalFollowup;
        private readonly AskUserQuestionSchemaRegistry _schemaRegistry;
        private readonly SubmitAskUserQuestionUseCase _submitUseCase;
        private readonly SidekickStoreService _storeService;

        private IAskUserQuestionView _view;
        private PendingPermission _currentPermission;
        private IAskUserQuestionSchemaAdapter _currentAdapter;
        private AskUserQuestionPrompt _currentPrompt;
        private AskUserQuestionSessionState _sessionState;
        private int _activeTabIndex;
        private Action<string, JObject> _applyAnswersToTimeline;
        private bool _disposed;

        private AskUserQuestionInput CurrentInput => _currentPrompt?.Input;

        public bool IsActive => _currentPermission != null;

        public AskUserQuestionViewModel(
            IRuntimeOrchestrator runtimeOrchestrator,
            ISettingsStore settingsStore,
            AskUserQuestionSchemaRegistry schemaRegistry = null,
            SidekickStoreService storeService = null,
            SubmitAskUserQuestionUseCase submitUseCase = null)
        {
            _runtimeOrchestrator = runtimeOrchestrator;
            _settingsStore = settingsStore;
            _schemaRegistry = schemaRegistry ?? AskUserQuestionSchemaRegistry.CreateDefault();
            _submitUseCase = submitUseCase ?? new SubmitAskUserQuestionUseCase(_schemaRegistry);
            _storeService = storeService;
        }

        /// <summary>
        /// Re-wires the <see cref="ComposerViewModel"/> reference when the provider scope
        /// changes. Call this from the factory immediately after creating the new
        /// <see cref="ComposerViewModel"/> for the scope.
        /// </summary>
        public void SetComposerViewModel(ComposerViewModel composerVm)
        {
            _composerViewModel = composerVm;
        }

        // === Commands ===

        [ICommand]
        private void Submit()
        {
            var dispatch = _submitUseCase?.Submit(_currentPermission, _currentPrompt, _sessionState);
            if (dispatch == null)
            {
                return;
            }

            PublishAnswersToTimeline();
            Dispatch(dispatch, _currentPermission);
            CloseOverlay();
        }

        [ICommand]
        private void Cancel()
        {
            if (_currentPermission == null)
            {
                return;
            }

            if (_settingsStore is { VerboseLogging: true })
            {
                Debug.Log("[AskUserQuestionViewModel] User cancelled");
            }

            var dispatch = _submitUseCase?.Cancel(_currentPermission, _currentPrompt);
            if (dispatch == null)
            {
                return;
            }

            PublishAnswersToTimeline();
            Dispatch(dispatch, _currentPermission);
            CloseOverlay();
        }

        // === View binding ===

        public void BindView(IAskUserQuestionView view)
        {
            if (_view != null)
            {
                _view.ClosedRequested -= OnClosedRequested;
                _view.SubmitRequested -= OnSubmitRequested;
                _view.QuestionTabChanged -= SwitchQuestionTab;
                _view.OptionToggled -= ToggleOption;
                _view.OtherTextChanged -= HandleOtherTextChanged;
            }

            _view = view;
            if (_view == null)
            {
                return;
            }

            _view.ClosedRequested += OnClosedRequested;
            _view.SubmitRequested += OnSubmitRequested;
            _view.QuestionTabChanged += SwitchQuestionTab;
            _view.OptionToggled += ToggleOption;
            _view.OtherTextChanged += HandleOtherTextChanged;
            RenderViewState();
        }

        // === Public methods ===

        /// <summary>
        /// Handle an AskUserQuestion or ExitPlanMode request.
        /// </summary>
        public void HandlePermission(PendingPermission permission)
        {
            if (_settingsStore is { VerboseLogging: true })
            {
                Debug.Log($"[AskUserQuestionViewModel] HandlePermission: toolUseId={permission?.ToolUseId}, requestId={permission?.RequestId}, toolName={permission?.ToolName}");
            }

            if (permission == null || _view == null)
            {
                return;
            }

            var adapter = _schemaRegistry.Resolve(permission);
            if (adapter == null)
            {
                Debug.LogWarning($"[AskUserQuestionViewModel] No schema adapter found for {permission.ToolName}");
                return;
            }

            var prompt = adapter.BuildPrompt(permission);
            if (prompt?.Input?.Questions == null || prompt.Input.Questions.Count == 0)
            {
                Debug.LogWarning("[AskUserQuestionViewModel] Invalid or empty questions input");

                if (permission.Kind == PendingPermissionKind.SessionUserInput)
                {
                    Dispatch(adapter.BuildCancel(permission, prompt), permission);
                }
                else
                {
                    _runtimeOrchestrator?.SendControlResponse(
                        permission.RequestId,
                        permission.ToolUseId,
                        allow: false,
                        updatedInput: null,
                        message: "Invalid AskUserQuestion input: no questions provided");
                }

                return;
            }

            _currentPermission = permission;
            _currentAdapter = adapter;
            _currentPrompt = prompt;
            _sessionState = new AskUserQuestionSessionState(prompt.Input);
            _activeTabIndex = 0;

            _composerViewModel?.SetInputEnabled(false);
            ShowOverlay(true);
            RenderViewState();
        }

        public void UpdateRuntime(IRuntimeOrchestrator runtimeOrchestrator)
        {
            _runtimeOrchestrator = runtimeOrchestrator;
        }

        public void SetApplyAnswersToTimeline(Action<string, JObject> callback)
        {
            _applyAnswersToTimeline = callback;
        }

        public void SetSubmitLocalFollowup(Action<string> callback)
        {
            _submitLocalFollowup = callback;
        }

        public void Reset()
        {
            if (_currentPermission == null)
            {
                return;
            }

            CloseOverlay();
        }

        // === IDisposable ===

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_view != null)
            {
                _view.ClosedRequested -= OnClosedRequested;
                _view.SubmitRequested -= OnSubmitRequested;
                _view.QuestionTabChanged -= SwitchQuestionTab;
                _view.OptionToggled -= ToggleOption;
                _view.OtherTextChanged -= HandleOtherTextChanged;
                _view.Render(AskUserQuestionViewState.Hidden);
                _view = null;
            }
        }

        // === Internal for tests ===

        internal void Dispatch(AskUserQuestionDispatch dispatch, PendingPermission permission)
        {
            if (dispatch == null || permission == null)
            {
                return;
            }

            ApplyModeTransition(dispatch.ModeTransition);

            if (!string.IsNullOrWhiteSpace(dispatch.LocalFollowupText))
            {
                _submitLocalFollowup?.Invoke(dispatch.LocalFollowupText);
            }

            if (dispatch.Channel == AskUserQuestionDispatchChannel.LocalOnly)
            {
                return;
            }

            if (dispatch.Channel == AskUserQuestionDispatchChannel.UserInputResponse)
            {
                if (_settingsStore is { VerboseLogging: true })
                {
                    Debug.Log($"[AskUserQuestionViewModel] Sending user_input response: {dispatch.UserInputResponse}");
                }

                _runtimeOrchestrator?.SendUserInputResponse(permission, dispatch.UserInputResponse);
                return;
            }

            if (_settingsStore is { VerboseLogging: true })
            {
                Debug.Log($"[AskUserQuestionViewModel] Sending control_response: allow={dispatch.Allow}, message={dispatch.Message}");
            }

            _runtimeOrchestrator?.SendControlResponse(
                permission.RequestId,
                permission.ToolUseId,
                dispatch.Allow,
                dispatch.UpdatedInput,
                dispatch.Message);
        }

        // === Private helpers ===

        private void OnClosedRequested() => CancelCommand.Execute(null);
        private void OnSubmitRequested() => SubmitCommand.Execute(null);

        private void SwitchQuestionTab(int index)
        {
            if (CurrentInput == null || index < 0 || index >= CurrentInput.Questions.Count)
            {
                return;
            }

            _activeTabIndex = index;
            RenderViewState();
        }

        private void ToggleOption(int questionIndex, int optionIndex, bool isMultiSelect)
        {
            if (_sessionState == null || CurrentInput == null)
            {
                return;
            }

            _sessionState.SelectOption(questionIndex, optionIndex, isMultiSelect);
            RenderViewState();

            var outcome = _submitUseCase.DecideAfterToggle(
                CurrentInput, _sessionState, questionIndex, optionIndex, _activeTabIndex, isMultiSelect);

            switch (outcome.Action)
            {
                case AskUserQuestionToggleAction.Submit:     SubmitCommand.Execute(null); break;
                case AskUserQuestionToggleAction.AdvanceTab: SwitchQuestionTab(outcome.NextTabIndex); break;
            }
        }

        private void HandleOtherTextChanged(string value)
        {
            if (_sessionState == null || _activeTabIndex < 0 || _activeTabIndex >= _sessionState.QuestionCount)
            {
                return;
            }

            _sessionState.SetOtherText(_activeTabIndex, value);
            RenderViewState();
        }

        private void RenderViewState()
        {
            if (_view == null)
            {
                return;
            }

            if (_sessionState == null || CurrentInput == null || _activeTabIndex < 0 || _activeTabIndex >= CurrentInput.Questions.Count)
            {
                _view.Render(AskUserQuestionViewState.Hidden);
                return;
            }

            var question = CurrentInput.Questions[_activeTabIndex];
            var selectedIndices = _sessionState.GetSelectedIndices(_activeTabIndex);
            var isOtherSelected = question.IsOther || selectedIndices.Any(index =>
                index >= 0 &&
                index < question.Options.Count &&
                AskUserQuestionSessionState.IsOtherOption(question.Options[index]));

            var tabs = CurrentInput.Questions
                .Select((item, index) => new AskUserQuestionTabViewState(
                    string.IsNullOrEmpty(item.Header) ? $"Q{index + 1}" : item.Header,
                    index == _activeTabIndex))
                .ToList();

            var options = question.Options
                .Select((option, index) => new AskUserQuestionOptionViewState(
                    option.Label,
                    option.Description,
                    selectedIndices.Contains(index),
                    AskUserQuestionSessionState.IsOtherOption(option)))
                .ToList();

            var badgeText = _sessionState.QuestionCount > 1 || question.MultiSelect || isOtherSelected
                ? $"{_sessionState.CountAnsweredQuestions()}/{_sessionState.QuestionCount} answered"
                : string.Empty;

            _view.Render(new AskUserQuestionViewState(
                headerText: string.IsNullOrEmpty(question.Header) ? "Question" : question.Header,
                questionText: question.Question ?? string.Empty,
                countBadgeText: badgeText,
                otherText: _sessionState.GetOtherText(_activeTabIndex),
                otherPlaceholder: string.IsNullOrWhiteSpace(question.OtherPlaceholder)
                    ? "Describe what to do instead"
                    : question.OtherPlaceholder,
                isVisible: true,
                isSubmitEnabled: _sessionState.AreAllQuestionsAnswered(),
                showOtherInput: isOtherSelected,
                isOtherInputSecret: false,
                isMultiSelect: question.MultiSelect,
                activeQuestionIndex: _activeTabIndex,
                tabs: tabs,
                options: options));
        }

        private void CloseOverlay()
        {
            ShowOverlay(false);
            _currentPermission = null;
            _currentAdapter = null;
            _currentPrompt = null;
            _sessionState = null;
            _activeTabIndex = 0;

            _composerViewModel?.SetInputEnabled(true);
        }

        private void ShowOverlay(bool show)
        {
            if (!show)
            {
                _view?.Render(AskUserQuestionViewState.Hidden);
            }
        }

        private void PublishAnswersToTimeline()
        {
            if (_applyAnswersToTimeline == null || string.IsNullOrEmpty(_currentPermission?.ToolUseId))
            {
                return;
            }

            var payload = AskUserQuestionTraceFormatter.BuildTraceAnswersPayload(_currentPrompt?.Input, _sessionState);
            _applyAnswersToTimeline(_currentPermission.ToolUseId, payload);
        }

        private void ApplyModeTransition(AskUserQuestionModeTransition transition)
        {
            if (transition == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(transition.CollaborationMode))
            {
                _settingsStore.CollaborationMode = transition.CollaborationMode;
            }

            if (!string.IsNullOrEmpty(transition.PermissionMode))
            {
                _settingsStore.PermissionMode = transition.PermissionMode;
            }

            if (_settingsStore is { VerboseLogging: true })
            {
                Debug.Log($"[AskUserQuestionViewModel] Switched modes to collaboration={_settingsStore.CollaborationMode} permission={_settingsStore.PermissionMode}");
            }
        }
    }
}
