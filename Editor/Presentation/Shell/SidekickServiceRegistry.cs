// SPDX-License-Identifier: GPL-3.0-only
using Unity.AppUI.MVVM;
using Ryx.Sidekick.Editor.UseCases.Attachments;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Conversations;
using Ryx.Sidekick.Editor.UseCases.Pro;
using Ryx.Sidekick.Editor.UseCases.Updates;
using Ryx.Sidekick.Editor.Presentation.Contracts;
using Ryx.Sidekick.Editor.Presentation.Renderers;
using Ryx.Sidekick.Editor.Presentation.State;
using Ryx.Sidekick.Editor.Presentation.ViewModels;
using Ryx.Sidekick.Editor.UseCases.Providers;
using Ryx.Sidekick.Editor.Infrastructure;
using Ryx.Sidekick.Editor.Infrastructure.Entitlements;
using Ryx.Sidekick.Editor.Infrastructure.Licensing;
using Ryx.Sidekick.Editor.Infrastructure.Net;
using Ryx.Sidekick.Editor.Infrastructure.Pro;
using Ryx.Sidekick.Editor.Infrastructure.UnityEditor;
using Ryx.Sidekick.Editor.Infrastructure.Updates;

namespace Ryx.Sidekick.Editor.Presentation.Shell
{
    /// <summary>
    /// Registers window-scope services into the DI container.
    /// </summary>
    internal static class SidekickServiceRegistry
    {
        internal static void RegisterWindowServices(IServiceCollection services)
        {
            services.AddSingleton<ISettingsStore, SidekickSettingsStore>();
            services.AddSingleton<IEditorDialogService, UnityEditorDialogService>();
            services.AddSingleton<IEditorScheduler, UnityEditorScheduler>();
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<IClipboardService, UnityClipboardService>();
            services.AddSingleton<IDragDropAttachmentSource, UnityDragDropService>();
            services.AddSingleton<IViewScreenshotService, UnityViewScreenshotService>();
            services.AddSingleton<IEditorSelectionService, UnityEditorSelectionService>();
            services.AddSingleton<ICredentialStoreProvider, DefaultCredentialStoreProvider>();
            services.AddSingleton<IAuthService, ClaudeAuthService>();
            services.AddSingleton<ISidekickAccountService, SidekickAccountService>();
            services.AddSingleton<IMcpForUnityGateway, McpForUnityGateway>();
            services.AddSingleton<IMarkdownContentRenderer, MarkdownContentRenderer>();
            services.AddSingleton<IToolElementRendererFactory, DefaultToolElementRendererFactory>();
            services.AddSingleton<IToolRendererRegistry, ToolRendererRegistry>();
            services.AddSingleton<IAttachmentElementFactory, AttachmentElementFactory>();
            services.AddSingleton<IProviderCatalog, CliProviderCatalogAdapter>();
            // Agent Host (out-of-process CLI ownership; feature-flagged by SidekickSettings.UseAgentHost,
            // default OFF). The real Phase 4 connector materializes the shipped daemon payload, resolves
            // the bundled .NET runtime, and reuses-or-spawns the per-project daemon — but ONLY when the
            // flag is ON (DefaultProcessHostFactory short-circuits on the flag, so TryConnect is never
            // called with the flag OFF). Any connector failure returns IsValid=false ⇒ the factory falls
            // back to the in-process CliProcessHost (zero regression). WindowScopedRuntimeLeaseManager
            // receives the factory via constructor injection and threads it into ProcessManager (no
            // global statics — CLAUDE.md).
            services.AddSingleton<IAgentHostConnector, AgentHostConnector>();
            services.AddSingleton<IProcessHostFactory, DefaultProcessHostFactory>();
            services.AddSingleton<IRuntimeLeaseManager, WindowScopedRuntimeLeaseManager>();
            services.AddSingleton<IResumeStateStore, SessionStateResumeStateStore>();
            services.AddSingleton<ILogger, UnitySidekickLogger>();
            services.AddSingleton<SidekickStoreService>();
            services.AddSingleton<ISidekickControllerGraphFactory, SidekickControllerGraphFactory>();

            // Attachment use cases.
            // NOTE: AttachmentSessionState + Add*/RemoveAttachmentUseCase are intentionally
            // NOT registered here — they are window-scoped state (one set per Sidekick window)
            // and are constructed manually in SidekickControllerGraphFactory.CreateWindowScopeGraph
            // because the factory currently does not receive an IServiceScope. Migration to a
            // proper DI resolve is deferred to the asmdef split in T10/T11.
            services.AddSingleton<BuildPromptContextUseCase>();

            // Phase 5 provider-switch use cases.
            services.AddSingleton<LoadProviderUiStateUseCase>();
            services.AddSingleton<SaveProviderUiStateUseCase>();
            services.AddSingleton<SwitchProviderUseCase>();

            // Pro paywall services.
            services.AddSingleton<IProPresence, SidekickProPresence>();
            services.AddSingleton<IHttpClient, UnityWebRequestHttpClient>();
            services.AddSingleton<IExternalUrlOpener, UnityExternalUrlOpener>();
            services.AddSingleton<RemoteConfigCache>();
            services.AddSingleton<IBakedConfigSource, BakedConfigSource>();
            services.AddSingleton<IRemoteConfigSource, RemoteConfigSource>();
            services.AddSingleton<GetProOfferQuery>();
            services.AddSingleton<ResolveLockedProvidersQuery>();

            // Entitlement-aware gate: combine package presence with offline-verified ownership so an
            // entitled-but-not-installed user sees "Install Pro" instead of the buy/upsell. All deps are
            // type-based (App UI DI has no factory-lambda registrations) — endpoints live as consts in
            // InstallProUseCase, the key in DefaultEntitlementVerifier.
            services.AddSingleton<IEntitlementCache, SettingsEntitlementCache>();
            services.AddSingleton<IEntitlementVerifier, DefaultEntitlementVerifier>();
            services.AddSingleton<IProEntitlement, LicenseProEntitlement>();
            services.AddSingleton<ResolveProAccessStateQuery>();
            services.AddSingleton<IFileDownloader, UnityFileDownloader>();
            services.AddSingleton<IPackageInstaller, EditorPackageInstaller>();
            services.AddSingleton<IProInstaller, InstallProUseCase>();
            // Same use case behind the SKU-agnostic contract used by the status-bar "Update" chip
            // (lite or pro). Stateless → a separate singleton instance is harmless.
            services.AddSingleton<IUpdateInstaller, InstallProUseCase>();

            services.AddSingleton<PaywallViewModel>();

            // Update-notification services (spec A6).
            // UpdateNotifier + UpdateNotificationViewModel are NOT registered here — they require a
            // live VisualElement reference view that is only available after the window panel boots.
            // They are constructed in WindowViewBindingPresenter.BindUpdates(), mirroring BindPaywall.
            services.AddSingleton<IInstalledPackageVersions, PackageManagerInstalledVersions>();
            services.AddSingleton<IDismissStore, EditorPrefsDismissStore>();
            services.AddSingleton<CheckForUpdatesQuery>();
        }
    }
}
