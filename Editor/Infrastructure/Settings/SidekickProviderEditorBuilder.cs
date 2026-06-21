// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Providers;
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Renders the provider-agnostic settings fields shared by every provider's settings page
    /// (CLI path, model, reasoning effort, collaboration/permission mode, extended thinking).
    /// Provider-specific options (e.g. Claude's Bedrock) are appended by the provider's own page,
    /// which lives in that provider's package (Lite for Claude, Pro for Codex/Cursor).
    /// All edits go through the per-provider snapshot API and do not change the active provider.
    /// </summary>
    internal static class SidekickProviderEditorBuilder
    {
        /// <summary>
        /// Appends the common provider fields to <paramref name="container"/> for <paramref name="providerId"/>.
        /// <paramref name="rebuild"/> is invoked when a structural change (model/collaboration) requires
        /// re-rendering dependent fields — the caller should clear and rebuild the whole page content.
        /// </summary>
        public static void BuildCommon(VisualElement container, string providerId, Action rebuild)
        {
            var settings = SidekickSettings.instance;
            var provider = CliProviderRegistry.GetProvider(providerId);
            var snapshot = settings.GetProviderUiState(providerId);

            void Save(Action<ProviderUiStateSnapshot> mutate)
            {
                var current = settings.GetProviderUiState(providerId);
                mutate(current);
                settings.SaveProviderUiState(current);
            }

            // CLI path (per provider, per platform)
            var cliSection = SidekickSettingsSectionBuilder.Section("CLI");
            container.Add(cliSection);
            var cliPath = new TextField { value = settings.GetCliPath(providerId) };
            cliPath.RegisterValueChangedCallback(evt => settings.SetCliPath(providerId, evt.newValue));
            cliSection.Add(SidekickSettingsSectionBuilder.BrowseRow("CLI Path", cliPath,
                () => EditorUtility.OpenFilePanel($"Select {provider.DisplayName}", "", ""),
                $"Path to the {provider.DisplayName} executable (per platform)."));

            var modelSection = SidekickSettingsSectionBuilder.Section("Model & Modes");
            container.Add(modelSection);

            // Model
            var presets = settings.GetModelCatalog(providerId)?.Models?
                              .Where(m => !string.IsNullOrWhiteSpace(m?.Id))
                              .Select(m => m.Id)
                              .ToList()
                          ?? provider.ModelPresets.ToList();
            var isCustomModel = !presets.Contains(snapshot.Model);
            var modelChoices = new List<string>(presets) { "Custom" };
            var modelDropdown = new DropdownField(modelChoices,
                isCustomModel ? modelChoices.Count - 1 : Math.Max(0, presets.IndexOf(snapshot.Model)));
            modelDropdown.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != "Custom")
                {
                    Save(s => s.Model = evt.newValue);
                    rebuild?.Invoke();
                }
            });
            modelSection.Add(SidekickSettingsSectionBuilder.FieldRow("Model", modelDropdown,
                "Model preset, or choose Custom and enter a full model id below."));

            var customModel = new TextField { value = isCustomModel ? snapshot.Model : string.Empty };
            customModel.RegisterCallback<FocusOutEvent>(_ =>
            {
                var value = customModel.value?.Trim();
                if (!string.IsNullOrEmpty(value) && value != snapshot.Model)
                {
                    Save(s => s.Model = value);
                    rebuild?.Invoke();
                }
            });
            modelSection.Add(SidekickSettingsSectionBuilder.FieldRow("Custom Model ID", customModel,
                "Full model identifier (used when Model is set to Custom)."));

            // Reasoning effort
            var catalog = settings.GetModelCatalog(providerId) ?? ProviderModelCatalogFactory.FromProvider(provider);
            var selectedModel = catalog?.Models?.FirstOrDefault(m =>
                string.Equals(m?.Id, snapshot.Model, StringComparison.Ordinal));
            var efforts = selectedModel?.SupportedReasoningEfforts?
                .Where(e => !string.IsNullOrWhiteSpace(e?.Value))
                .Select(e => e.Value)
                .ToList();
            if (efforts != null && efforts.Count > 0)
            {
                var effortIndex = Math.Max(0, efforts.IndexOf(snapshot.ReasoningEffort));
                var effortDropdown = new DropdownField(efforts, effortIndex);
                effortDropdown.RegisterValueChangedCallback(evt => Save(s => s.ReasoningEffort = evt.newValue));
                modelSection.Add(SidekickSettingsSectionBuilder.FieldRow("Reasoning Effort", effortDropdown));
            }

            // Collaboration mode
            var collaborationValue = snapshot.CollaborationMode;
            if (provider.CollaborationModes.Length > 1)
            {
                var values = CliProviderRegistry.GetCollaborationModeValues(provider);
                var labels = CliProviderRegistry.GetCollaborationModeLabels(provider);
                var index = Math.Max(0, Array.IndexOf(values, collaborationValue));
                var dropdown = new DropdownField(labels.ToList(), index);
                dropdown.RegisterValueChangedCallback(evt =>
                {
                    var i = Array.IndexOf(labels, evt.newValue);
                    if (i >= 0)
                    {
                        Save(s => s.CollaborationMode = values[i]);
                        rebuild?.Invoke();
                    }
                });
                modelSection.Add(SidekickSettingsSectionBuilder.FieldRow("Collaboration Mode", dropdown,
                    "How the agent should collaborate on the task."));
            }

            // Permission mode
            var permValues = CliProviderRegistry.GetPermissionModeValues(provider, collaborationValue);
            var permLabels = CliProviderRegistry.GetPermissionModeLabels(provider, collaborationValue);
            if (permValues.Length > 0)
            {
                var permIndex = Math.Max(0, Array.IndexOf(permValues, snapshot.PermissionMode));
                var permDropdown = new DropdownField(permLabels.ToList(), permIndex);
                permDropdown.RegisterValueChangedCallback(evt =>
                {
                    var i = Array.IndexOf(permLabels, evt.newValue);
                    if (i >= 0)
                    {
                        Save(s => s.PermissionMode = permValues[i]);
                    }
                });
                modelSection.Add(SidekickSettingsSectionBuilder.FieldRow("Permission Mode", permDropdown,
                    "Permission mode for tool usage."));
            }

            // Extended thinking (capability-gated, generic)
            if (provider.SupportsThinking)
            {
                var thinkingSection = SidekickSettingsSectionBuilder.Section("Thinking");
                container.Add(thinkingSection);

                var tokensField = new IntegerField { value = snapshot.MaxThinkingTokens };
                var tokensRow = SidekickSettingsSectionBuilder.FieldRow("Max Thinking Tokens", tokensField,
                    "Token budget for extended thinking.");
                tokensRow.style.display = snapshot.EnableThinking ? DisplayStyle.Flex : DisplayStyle.None;

                var thinkingToggle = new Toggle { value = snapshot.EnableThinking };
                thinkingToggle.RegisterValueChangedCallback(evt =>
                {
                    Save(s => s.EnableThinking = evt.newValue);
                    tokensRow.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                });
                thinkingSection.Add(SidekickSettingsSectionBuilder.FieldRow("Extended Thinking", thinkingToggle,
                    "Enable extended thinking mode (adds --max-thinking-tokens)."));

                tokensField.RegisterValueChangedCallback(evt => Save(s => s.MaxThinkingTokens = evt.newValue));
                thinkingSection.Add(tokensRow);
            }
        }
    }
}
