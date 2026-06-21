// SPDX-License-Identifier: GPL-3.0-only
using System;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Questions;

namespace Ryx.Sidekick.Editor.Presentation.Controllers
{
    /// <summary>
    /// Thin facade over <see cref="AskUserQuestionViewModel"/>.
    /// Controller for handling AskUserQuestion-style permission requests.
    /// </summary>
    internal sealed class AskUserQuestionController
    {
        public bool IsActive => ViewModel.IsActive;

        internal AskUserQuestionViewModel ViewModel { get; }

        public AskUserQuestionController(
            IRuntimeOrchestrator runtimeOrchestrator,
            ISettingsStore settingsStore,
            AskUserQuestionSchemaRegistry schemaRegistry = null,
            SidekickStoreService storeService = null,
            SubmitAskUserQuestionUseCase submitUseCase = null)
        {
            ViewModel = new AskUserQuestionViewModel(
                runtimeOrchestrator, settingsStore,
                schemaRegistry, storeService, submitUseCase);
        }

        public AskUserQuestionController(
            ProcessManager processManager,
            AskUserQuestionSchemaRegistry schemaRegistry = null,
            SidekickStoreService storeService = null)
            : this(
                processManager,
                new SidekickSettingsStore(),
                schemaRegistry,
                storeService)
        {
        }

        public void BindView(IAskUserQuestionView view) => ViewModel.BindView(view);
        public void HandlePermission(PendingPermission permission) => ViewModel.HandlePermission(permission);
        public void UpdateRuntime(IRuntimeOrchestrator runtimeOrchestrator) => ViewModel.UpdateRuntime(runtimeOrchestrator);
        public void SetApplyAnswersToTimeline(Action<string, JObject> callback) => ViewModel.SetApplyAnswersToTimeline(callback);
        public void SetSubmitLocalFollowup(Action<string> callback) => ViewModel.SetSubmitLocalFollowup(callback);

        /// <summary>
        /// Re-wires the <see cref="ComposerViewModel"/> reference when the provider scope
        /// changes. Delegates to <see cref="AskUserQuestionViewModel.SetComposerViewModel"/>.
        /// </summary>
        public void SetComposerViewModel(ComposerViewModel composerVm) =>
            ViewModel.SetComposerViewModel(composerVm);
        public void Reset() => ViewModel.Reset();

        internal void Dispatch(AskUserQuestionDispatch dispatch, PendingPermission permission) =>
            ViewModel.Dispatch(dispatch, permission);

        public void Submit() => ViewModel.SubmitCommand.Execute(null);
        public void Cancel() => ViewModel.CancelCommand.Execute(null);
    }
}
