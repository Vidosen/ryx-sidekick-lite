// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.Views;
using Ryx.Sidekick.Editor.Providers;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Pro;
using Unity.AppUI.MVVM;
using Unity.AppUI.Redux;
using Unity.Properties;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Presentation.ViewModels
{
    [ObservableObject]
    internal partial class ProviderSelectorViewModel : IDisposable
    {
        private readonly SidekickStoreService _storeService;
        private readonly IProviderCatalog _catalog;
        private readonly ISettingsStore _settingsStore;
        private readonly IDisposableSubscription _providerSubscription;

        // Optional Pro-paywall deps — null until T17 wires DI; kept null-safe throughout.
        private readonly IProPresence _proPresence;
        private readonly GetProOfferQuery _getProOffer;
        private readonly ResolveLockedProvidersQuery _resolveLockedProviders;

        private IProviderMenuView _view;
        private bool _disposed;
        private IProviderModelCatalogSource _modelCatalogSource;
        private ProviderModelCatalog _currentModelCatalog;
        private CancellationTokenSource _modelCatalogCancellation;
        private int _modelCatalogGeneration;
        private string _normalizedForModel;
        private bool _handlingProviderState;

        // === Observable properties ===

        private IReadOnlyList<ProviderOptionViewState> _providerOptions = Array.Empty<ProviderOptionViewState>();

        [CreateProperty]
        public IReadOnlyList<ProviderOptionViewState> ProviderOptions
        {
            get => _providerOptions;
            private set => SetProperty(ref _providerOptions, value);
        }

        private IReadOnlyList<ModelPresetViewState> _modelPresets = Array.Empty<ModelPresetViewState>();

        [CreateProperty]
        public IReadOnlyList<ModelPresetViewState> ModelPresets
        {
            get => _modelPresets;
            private set => SetProperty(ref _modelPresets, value);
        }

        private IReadOnlyList<ReasoningEffortViewState> _reasoningEfforts = Array.Empty<ReasoningEffortViewState>();

        [CreateProperty]
        public IReadOnlyList<ReasoningEffortViewState> ReasoningEfforts
        {
            get => _reasoningEfforts;
            private set => SetProperty(ref _reasoningEfforts, value);
        }

        private bool _isModelCatalogLoading;

        [CreateProperty]
        public bool IsModelCatalogLoading
        {
            get => _isModelCatalogLoading;
            private set => SetProperty(ref _isModelCatalogLoading, value);
        }

        private string _modelCatalogError = string.Empty;

        [CreateProperty]
        public string ModelCatalogError
        {
            get => _modelCatalogError;
            private set => SetProperty(ref _modelCatalogError, value ?? string.Empty);
        }

        private string _currentModel = string.Empty;

        [CreateProperty]
        public string CurrentModel
        {
            get => _currentModel;
            private set => SetProperty(ref _currentModel, value);
        }

        private bool _isThinkingEnabled;

        [CreateProperty]
        public bool IsThinkingEnabled
        {
            get => _isThinkingEnabled;
            private set => SetProperty(ref _isThinkingEnabled, value);
        }

        private bool _isThinkingSectionVisible;

        [CreateProperty]
        public bool IsThinkingSectionVisible
        {
            get => _isThinkingSectionVisible;
            private set => SetProperty(ref _isThinkingSectionVisible, value);
        }

        private bool _isProviderPopupOpen;

        [CreateProperty]
        public bool IsProviderPopupOpen
        {
            get => _isProviderPopupOpen;
            private set => SetProperty(ref _isProviderPopupOpen, value);
        }

        private bool _isModelPopupOpen;

        [CreateProperty]
        public bool IsModelPopupOpen
        {
            get => _isModelPopupOpen;
            private set => SetProperty(ref _isModelPopupOpen, value);
        }

        private string _customModelDraft = string.Empty;

        [CreateProperty]
        public string CustomModelDraft
        {
            get => _customModelDraft;
            private set => SetProperty(ref _customModelDraft, value);
        }

        // === Outgoing notifications to host ===

        public event global::System.Action<string> ProviderSwitchRequested;
        public event global::System.Action InterruptRuntimeRequested;

        // === Constructor ===

        public ProviderSelectorViewModel(
            SidekickStoreService store,
            IProviderCatalog catalog,
            ISettingsStore settingsStore,
            IProPresence proPresence = null,
            GetProOfferQuery getProOffer = null,
            ResolveLockedProvidersQuery resolveLockedProviders = null)
        {
            _storeService = store ?? throw new ArgumentNullException(nameof(store));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            _proPresence = proPresence;
            _getProOffer = getProOffer;
            _resolveLockedProviders = resolveLockedProviders;

            _providerSubscription = _storeService.SubscribeToProvider(HandleProviderStateChanged, fireImmediately: true);
        }

        // === Commands ===

        [ICommand]
        private void ToggleProviderPopup()
        {
            if (IsProviderPopupOpen)
            {
                IsProviderPopupOpen = false;
            }
            else
            {
                // Rebuild provider options (including locked Pro rows) so a freshly-fetched remote
                // offer is reflected the moment the popover opens, not only on the next provider-state change.
                var state = _storeService?.CurrentProviderState ?? ProviderState.Default;
                HandleProviderStateChanged(state);

                IsProviderPopupOpen = true;
                IsModelPopupOpen = false;
            }
        }

        [ICommand]
        private void ToggleModelPopup()
        {
            if (IsModelPopupOpen)
            {
                IsModelPopupOpen = false;
            }
            else
            {
                // Pre-fill draft if current model is not in presets
                var presets = _modelPresets;
                var isCustom = !presets.Any(p => string.Equals(p.Name, _currentModel, StringComparison.Ordinal));
                if (isCustom)
                {
                    CustomModelDraft = _currentModel;
                }

                IsModelPopupOpen = true;
                IsProviderPopupOpen = false;
            }
        }

        [ICommand]
        private void CloseProviderPopup()
        {
            IsProviderPopupOpen = false;
        }

        [ICommand]
        private void CloseModelPopup()
        {
            IsModelPopupOpen = false;
        }

        [ICommand]
        private void SelectProvider(string providerId)
        {
            CloseProviderPopup();
            CloseModelPopup();
            ProviderSwitchRequested?.Invoke(providerId);
        }

        [ICommand]
        private void SelectModelPreset(string preset)
        {
            // Keep the popup open: picking a model often reveals reasoning-effort
            // options the user still wants to choose from. Dismissal stays with the
            // outside-click / toggle paths.
            _settingsStore.Model = preset;
        }

        [ICommand]
        private void SelectReasoningEffort(string effort)
        {
            _settingsStore.ReasoningEffort = effort ?? string.Empty;
        }

        [ICommand]
        private void RetryModelCatalogLoad()
        {
            _ = RefreshModelCatalogAsync();
        }

        public void BindModelCatalogSource(string providerId, IProviderModelCatalogSource source)
        {
            _modelCatalogCancellation?.Cancel();
            _modelCatalogCancellation?.Dispose();
            _modelCatalogCancellation = new CancellationTokenSource();
            _modelCatalogGeneration++;
            _modelCatalogSource = source;
            _currentModelCatalog = ResolveInitialCatalog(providerId);
            RefreshModelSelection(_storeService.CurrentProviderState ?? ProviderState.Default, forceNormalize: true);

            if (_modelCatalogSource != null)
            {
                _ = RefreshModelCatalogAsync();
            }
            else
            {
                IsModelCatalogLoading = false;
                ModelCatalogError = string.Empty;
            }
        }

        [ICommand]
        private void ApplyCustomModel(string customModel)
        {
            if (string.IsNullOrWhiteSpace(customModel))
            {
                return;
            }

            _settingsStore.Model = customModel.Trim();
            CloseModelPopup();
        }

        [ICommand]
        private void CycleCollaborationMode()
        {
            var providerId = _storeService.CurrentProviderState?.ProviderId;
            var metadata = string.IsNullOrWhiteSpace(providerId)
                ? null
                : _catalog.GetProvider(providerId)?.Metadata;
            var modes = metadata?.CollaborationModes ?? Array.Empty<CollaborationModeDescriptor>();

            if (modes.Length <= 1)
            {
                return;
            }

            var currentMode = _settingsStore.CollaborationMode;
            var currentIndex = Array.FindIndex(modes, mode =>
                string.Equals(mode.Value, currentMode, StringComparison.Ordinal));
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var nextIndex = (currentIndex + 1) % modes.Length;
            _settingsStore.CollaborationMode = modes[nextIndex].Value;
            InterruptRuntimeRequested?.Invoke();
        }

        [ICommand]
        private void CyclePermissionMode()
        {
            var providerId = _storeService.CurrentProviderState?.ProviderId;
            var metadata = string.IsNullOrWhiteSpace(providerId)
                ? null
                : _catalog.GetProvider(providerId)?.Metadata;
            var collaborationMode = _settingsStore.CollaborationMode;
            var modes = metadata?.GetPermissionModes(collaborationMode) ?? Array.Empty<PermissionModeDescriptor>();

            if (modes.Length == 0)
            {
                return;
            }

            var currentMode = _settingsStore.PermissionMode;
            var currentIndex = Array.FindIndex(modes, mode =>
                string.Equals(mode.Value, currentMode, StringComparison.Ordinal));
            if (currentIndex < 0)
            {
                currentIndex = 0;
            }

            var nextIndex = (currentIndex + 1) % modes.Length;
            _settingsStore.PermissionMode = modes[nextIndex].Value;
            InterruptRuntimeRequested?.Invoke();
        }

        [ICommand]
        private void CyclePrimaryMode()
        {
            var providerId = _storeService.CurrentProviderState?.ProviderId;
            var metadata = string.IsNullOrWhiteSpace(providerId)
                ? null
                : _catalog.GetProvider(providerId)?.Metadata;
            var collabModes = metadata?.CollaborationModes ?? Array.Empty<CollaborationModeDescriptor>();

            if (collabModes.Length > 1)
            {
                CycleCollaborationModeCommand.Execute(null);
            }
            else
            {
                CyclePermissionModeCommand.Execute(null);
            }
        }

        [ICommand]
        private void SetThinkingEnabled(bool enabled)
        {
            _settingsStore.EnableThinking = enabled;
            IsThinkingEnabled = enabled;
        }

        // === View binding ===

        public void BindView(IProviderMenuView view)
        {
            // Detach old view
            if (_view != null)
            {
                PropertyChanged -= OnVmPropertyChanged;
                _view.ProviderOptionSelected -= OnProviderOptionSelected;
                _view.ModelPresetSelected -= OnModelPresetSelected;
                _view.CustomModelApplied -= OnCustomModelApplied;
                _view.ThinkingChanged -= OnThinkingChanged;
                _view.ReasoningEffortSelected -= OnReasoningEffortSelected;
                _view.ModelCatalogRetryRequested -= OnModelCatalogRetryRequested;
                _view.ProviderRequested -= OnProviderRequested;
                _view.ModelRequested -= OnModelRequested;
                _view.CollaborationModeRequested -= OnCollaborationModeRequested;
                _view.PermissionModeRequested -= OnPermissionModeRequested;
                _view.ProviderPopupDismissed -= OnProviderPopupDismissed;
                _view.ModelPopupDismissed -= OnModelPopupDismissed;
            }

            _view = view;

            if (_view == null)
            {
                return;
            }

            PropertyChanged += OnVmPropertyChanged;

            // Initial flush
            OnVmPropertyChanged(this, new PropertyChangedEventArgs(null));

            _view.ProviderOptionSelected += OnProviderOptionSelected;
            _view.ModelPresetSelected += OnModelPresetSelected;
            _view.CustomModelApplied += OnCustomModelApplied;
            _view.ThinkingChanged += OnThinkingChanged;
            _view.ReasoningEffortSelected += OnReasoningEffortSelected;
            _view.ModelCatalogRetryRequested += OnModelCatalogRetryRequested;
            _view.ProviderRequested += OnProviderRequested;
            _view.ModelRequested += OnModelRequested;
            _view.CollaborationModeRequested += OnCollaborationModeRequested;
            _view.PermissionModeRequested += OnPermissionModeRequested;
            _view.ProviderPopupDismissed += OnProviderPopupDismissed;
            _view.ModelPopupDismissed += OnModelPopupDismissed;
        }

        private void OnProviderPopupDismissed()
        {
            if (IsProviderPopupOpen)
                CloseProviderPopupCommand.Execute(null);
        }

        private void OnModelPopupDismissed()
        {
            if (IsModelPopupOpen)
                CloseModelPopupCommand.Execute(null);
        }

        // === IDisposable ===

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _providerSubscription?.Dispose();
            _modelCatalogCancellation?.Cancel();
            _modelCatalogCancellation?.Dispose();

            if (_view != null)
            {
                PropertyChanged -= OnVmPropertyChanged;
                _view.ProviderOptionSelected -= OnProviderOptionSelected;
                _view.ModelPresetSelected -= OnModelPresetSelected;
                _view.CustomModelApplied -= OnCustomModelApplied;
                _view.ThinkingChanged -= OnThinkingChanged;
                _view.ReasoningEffortSelected -= OnReasoningEffortSelected;
                _view.ModelCatalogRetryRequested -= OnModelCatalogRetryRequested;
                _view.ProviderRequested -= OnProviderRequested;
                _view.ModelRequested -= OnModelRequested;
                _view.CollaborationModeRequested -= OnCollaborationModeRequested;
                _view.PermissionModeRequested -= OnPermissionModeRequested;
                _view.ProviderPopupDismissed -= OnProviderPopupDismissed;
                _view.ModelPopupDismissed -= OnModelPopupDismissed;
                _view = null;
            }
        }

        // === Private helpers ===

        private void HandleProviderStateChanged(ProviderState state)
        {
            // Hard re-entrancy guard: normalization below writes ReasoningEffort, which raises
            // ActiveProviderStateChanged and re-enters this handler synchronously through the store
            // subscription. Swallow the nested call — the outer invocation renders the final state.
            if (_handlingProviderState)
            {
                return;
            }

            _handlingProviderState = true;
            try
            {
                HandleProviderStateChangedCore(state);
            }
            finally
            {
                _handlingProviderState = false;
            }
        }

        private void HandleProviderStateChangedCore(ProviderState state)
        {
            state ??= ProviderState.Default;

            var providerId = state.ProviderId ?? string.Empty;
            var providerModule = _catalog.GetProvider(providerId);
            var metadata = providerModule?.Metadata;

            // Rebuild provider options (normal rows)
            var allProviders = _catalog.AllProviders;
            var normalCount = allProviders?.Count ?? 0;
            var normalOptions = new List<ProviderOptionViewState>(normalCount);
            if (allProviders != null)
            {
                foreach (var p in allProviders)
                {
                    normalOptions.Add(new ProviderOptionViewState(
                        p.Id,
                        p.Metadata?.DisplayName ?? p.Id,
                        string.Equals(p.Id, providerId, StringComparison.Ordinal)));
                }
            }

            // Append locked Pro rows when the Pro package is absent and deps are wired.
            if (_proPresence != null && !_proPresence.IsInstalled
                && _getProOffer != null && _resolveLockedProviders != null)
            {
                var offer = _getProOffer.Get();
                if (offer != null)
                {
                    var locked = _resolveLockedProviders.Resolve(offer, _catalog);
                    if (locked != null)
                    {
                        foreach (var feature in locked)
                        {
                            if (feature == null) continue;
                            normalOptions.Add(new ProviderOptionViewState(
                                feature.Id,
                                feature.DisplayName ?? feature.Id,
                                isActive: false,
                                isLocked: true,
                                featureId: feature.Id));
                        }
                    }
                }
            }

            ProviderOptions = normalOptions.ToArray();

            if (_currentModelCatalog == null
                || !string.Equals(_currentModelCatalog.ProviderId, providerId, StringComparison.Ordinal))
            {
                _currentModelCatalog = ResolveInitialCatalog(providerId);
            }
            RefreshModelSelection(state, forceNormalize: false);
            IsThinkingSectionVisible = metadata?.SupportsThinking ?? false;
            IsThinkingEnabled = _settingsStore.EnableThinking;
        }

        private ProviderModelCatalog ResolveInitialCatalog(string providerId)
        {
            var cached = _settingsStore.GetModelCatalog(providerId);
            if (cached?.Models?.Count > 0)
            {
                return cached;
            }

            return _catalog.GetProvider(providerId)?.Metadata?.FallbackModelCatalog
                ?? new ProviderModelCatalog(providerId, Array.Empty<ModelDescriptor>());
        }

        // Renders model/effort options and, when the supported-effort set may have changed (model or
        // catalog switch), normalizes the stored effort exactly once. Normalization is kept out of the
        // pure render path so unrelated provider-state changes (permission/collaboration) don't trigger
        // redundant settings writes + re-entrant store dispatches.
        private void RefreshModelSelection(ProviderState state, bool forceNormalize)
        {
            var currentModel = state?.Model ?? string.Empty;
            if (forceNormalize || !string.Equals(currentModel, _normalizedForModel, StringComparison.Ordinal))
            {
                // Set the guard before writing so the re-entrant dispatch raised by the settings setter
                // sees the model as already normalized and skips a second pass.
                _normalizedForModel = currentModel;
                NormalizeReasoningEffortForCurrentModel(state);
            }

            RebuildModelOptions(state);
        }

        private void NormalizeReasoningEffortForCurrentModel(ProviderState state)
        {
            var selectedModel = FindModel(state?.Model ?? string.Empty);
            var efforts = selectedModel?.SupportedReasoningEfforts ?? new List<ReasoningEffortDescriptor>();
            if (efforts.Count == 0)
            {
                return;
            }

            var stored = _settingsStore.ReasoningEffort ?? string.Empty;
            if (efforts.Any(effort => string.Equals(effort.Value, stored, StringComparison.Ordinal)))
            {
                return;
            }

            _settingsStore.ReasoningEffort = selectedModel.DefaultReasoningEffort ?? string.Empty;
        }

        private void RebuildModelOptions(ProviderState state)
        {
            var currentModel = state?.Model ?? string.Empty;
            var models = _currentModelCatalog?.Models ?? new List<ModelDescriptor>();
            ModelPresets = models
                .Where(model => !string.IsNullOrWhiteSpace(model?.Id))
                .Select(model => new ModelPresetViewState(
                    model.Id,
                    string.Equals(model.Id, currentModel, StringComparison.Ordinal),
                    model.DisplayName,
                    model.Description))
                .ToArray();
            CurrentModel = currentModel;

            var selectedModel = FindModel(currentModel);
            var efforts = selectedModel?.SupportedReasoningEfforts ?? new List<ReasoningEffortDescriptor>();
            var selectedEffort = ResolveEffectiveEffort(selectedModel, efforts);

            ReasoningEfforts = efforts
                .Select(effort => new ReasoningEffortViewState(
                    effort.Value,
                    effort.Description,
                    string.Equals(effort.Value, selectedEffort, StringComparison.Ordinal)))
                .ToArray();
        }

        private ModelDescriptor FindModel(string modelId)
        {
            return (_currentModelCatalog?.Models ?? new List<ModelDescriptor>())
                .FirstOrDefault(model => string.Equals(model?.Id, modelId, StringComparison.Ordinal));
        }

        // Display-only: returns the stored effort when supported, otherwise the model default. Never writes.
        private string ResolveEffectiveEffort(ModelDescriptor selectedModel, IReadOnlyList<ReasoningEffortDescriptor> efforts)
        {
            var stored = _settingsStore.ReasoningEffort ?? string.Empty;
            if (efforts.Count == 0 || efforts.Any(effort => string.Equals(effort.Value, stored, StringComparison.Ordinal)))
            {
                return stored;
            }

            return selectedModel?.DefaultReasoningEffort ?? string.Empty;
        }

        private async Task RefreshModelCatalogAsync()
        {
            var source = _modelCatalogSource;
            var cancellation = _modelCatalogCancellation;
            var generation = _modelCatalogGeneration;
            if (source == null || cancellation == null)
            {
                return;
            }

            IsModelCatalogLoading = true;
            ModelCatalogError = string.Empty;
            try
            {
                var catalog = await source.LoadModelsAsync(cancellation.Token);
                if (_disposed || cancellation.IsCancellationRequested || generation != _modelCatalogGeneration)
                {
                    return;
                }

                if (catalog?.Models?.Count > 0)
                {
                    _currentModelCatalog = catalog;
                    _settingsStore.SaveModelCatalog(catalog);
                    RefreshModelSelection(_storeService.CurrentProviderState ?? ProviderState.Default, forceNormalize: true);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (generation == _modelCatalogGeneration)
                {
                    ModelCatalogError = ex.Message;
                    Debug.LogWarning($"[Ryx Sidekick] Failed to refresh model catalog: {ex.Message}");
                }
            }
            finally
            {
                if (generation == _modelCatalogGeneration)
                {
                    IsModelCatalogLoading = false;
                }
            }
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_view == null)
            {
                return;
            }

            var name = e.PropertyName;

            if (name is null or nameof(ProviderOptions))
            {
                _view.RenderProviderOptions(ProviderOptions);
            }

            if (name is null or nameof(ModelPresets))
            {
                _view.RenderModelPresets(ModelPresets);
            }

            if (name is null or nameof(ReasoningEfforts))
            {
                _view.RenderReasoningEfforts(ReasoningEfforts);
            }

            if (name is null or nameof(IsModelCatalogLoading) or nameof(ModelCatalogError))
            {
                _view.SetModelCatalogStatus(IsModelCatalogLoading, ModelCatalogError);
            }

            if (name is null or nameof(IsThinkingSectionVisible))
            {
                _view.ShowThinkingSection(IsThinkingSectionVisible);
            }

            if (name is null or nameof(IsThinkingEnabled))
            {
                _view.SetThinkingEnabled(IsThinkingEnabled);
            }

            if (name is null or nameof(IsProviderPopupOpen))
            {
                _view.ShowProviderPopup(IsProviderPopupOpen);
            }

            if (name is null or nameof(IsModelPopupOpen))
            {
                _view.ShowModelPopup(IsModelPopupOpen);
            }
        }

        private void OnProviderOptionSelected(string id) => SelectProviderCommand.Execute(id);
        private void OnModelPresetSelected(string name) => SelectModelPresetCommand.Execute(name);
        private void OnCustomModelApplied(string text) => ApplyCustomModelCommand.Execute(text);
        private void OnThinkingChanged(bool enabled) => SetThinkingEnabledCommand.Execute(enabled);
        private void OnReasoningEffortSelected(string effort) => SelectReasoningEffortCommand.Execute(effort);
        private void OnModelCatalogRetryRequested() => RetryModelCatalogLoadCommand.Execute(null);
        private void OnProviderRequested() => ToggleProviderPopupCommand.Execute(null);
        private void OnModelRequested() => ToggleModelPopupCommand.Execute(null);
        private void OnCollaborationModeRequested() => CycleCollaborationModeCommand.Execute(null);
        private void OnPermissionModeRequested() => CyclePermissionModeCommand.Execute(null);
    }
}
