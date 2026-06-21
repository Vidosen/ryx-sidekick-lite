// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Providers;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>
    /// UI-facing metadata for a CLI provider — display name, model presets,
    /// collaboration/permission modes — surfaced to the provider selector,
    /// status bar, and Redux selectors without exposing the full
    /// <c>ICliProvider</c> surface.
    /// </summary>
    internal interface IProviderUiMetadata
    {
        string Id { get; }
        string DisplayName { get; }
        string[] ModelPresets { get; }
        ProviderModelCatalog FallbackModelCatalog => ProviderModelCatalogFactory.FromPresets(Id, ModelPresets, DefaultModel);
        string DefaultModel { get; }
        AuthOnboardingKind AuthKind { get; }
        CollaborationModeDescriptor[] CollaborationModes { get; }
        PermissionModeDescriptor[] GetPermissionModes(string collaborationMode);
        ProviderModeSelection NormalizeModeSelection(string collaborationMode, string permissionMode);
        bool IsAutoApprovePermissionMode(string permissionMode);
        bool SupportsThinking { get; }
    }
}
