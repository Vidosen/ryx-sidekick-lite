// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.Domain.Commands;

namespace Ryx.Sidekick.Editor.Providers
{
    [Serializable]
    internal sealed class ReasoningEffortDescriptor
    {
        public string Value;
        public string Description;

        public ReasoningEffortDescriptor()
        {
        }

        public ReasoningEffortDescriptor(string value, string description = null)
        {
            Value = value ?? string.Empty;
            Description = description ?? string.Empty;
        }
    }

    [Serializable]
    internal sealed class ModelDescriptor
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public bool IsDefault;
        public string DefaultReasoningEffort;
        public List<ReasoningEffortDescriptor> SupportedReasoningEfforts = new();

        public ModelDescriptor()
        {
        }

        public ModelDescriptor(
            string id,
            string displayName = null,
            bool isDefault = false,
            IEnumerable<ReasoningEffortDescriptor> supportedReasoningEfforts = null,
            string defaultReasoningEffort = null,
            string description = null)
        {
            Id = id ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Description = description ?? string.Empty;
            IsDefault = isDefault;
            DefaultReasoningEffort = defaultReasoningEffort ?? string.Empty;
            SupportedReasoningEfforts = supportedReasoningEfforts?.ToList() ?? new List<ReasoningEffortDescriptor>();
        }
    }

    [Serializable]
    internal sealed class ProviderModelCatalog
    {
        public string ProviderId;
        public string CliVersion;
        public long RefreshedAtUnixSeconds;
        public List<ModelDescriptor> Models = new();

        public ProviderModelCatalog()
        {
        }

        public ProviderModelCatalog(string providerId, IEnumerable<ModelDescriptor> models, string cliVersion = null)
        {
            ProviderId = providerId ?? string.Empty;
            CliVersion = cliVersion ?? string.Empty;
            RefreshedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Models = models?.ToList() ?? new List<ModelDescriptor>();
        }
    }

    internal interface IProviderModelCatalogSource
    {
        Task<ProviderModelCatalog> LoadModelsAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Source that can supply the live list of slash commands for a provider scope.
    /// </summary>
    internal interface IProviderSlashCommandSource
    {
        Task<IReadOnlyList<SlashCommand>> LoadCommandsAsync(CancellationToken cancellationToken);
    }

    internal static class ProviderModelCatalogFactory
    {
        // Each provider declares its own model/effort table via ICliProvider.BuildFallbackModelCatalog;
        // this factory just delegates so provider-specific knowledge stays out of the shared Domain layer.
        public static ProviderModelCatalog FromProvider(ICliProvider provider)
        {
            return provider?.BuildFallbackModelCatalog() ?? new ProviderModelCatalog();
        }

        public static ProviderModelCatalog FromPresets(
            string providerId,
            IEnumerable<string> presets,
            string defaultModel,
            IEnumerable<ReasoningEffortDescriptor> supportedReasoningEfforts = null,
            string defaultReasoningEffort = null)
        {
            var efforts = supportedReasoningEfforts?.ToList() ?? new List<ReasoningEffortDescriptor>();
            return new ProviderModelCatalog(
                providerId,
                (presets ?? Array.Empty<string>()).Select(model => new ModelDescriptor(
                    model,
                    isDefault: string.Equals(model, defaultModel, StringComparison.Ordinal),
                    supportedReasoningEfforts: efforts.Select(effort => new ReasoningEffortDescriptor(effort.Value, effort.Description)),
                    defaultReasoningEffort: defaultReasoningEffort)));
        }
    }
}
