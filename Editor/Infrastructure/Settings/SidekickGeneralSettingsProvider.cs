// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Providers;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// General settings page (Project/Sidekick). Holds only project-wide options; provider-specific
    /// configuration lives under Project/Sidekick/Providers and MCP servers under Project/Sidekick/MCP.
    /// Plain UI Toolkit (no App UI).
    /// </summary>
    internal class SidekickSettingsProvider : SettingsProvider
    {
        private static string OnboardingCompletedVersionKey => SidekickAppConstants.EditorPrefsKeys.OnboardingCompletedVersion;

        public SidekickSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SidekickSettingsProvider("Project/Sidekick", SettingsScope.Project)
            {
                keywords = new[] { "Sidekick", "AI", "Code", "CLI", "Anthropic", "working directory", "debug" }
            };
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            var settings = SidekickSettings.instance;
            var root = SidekickSettingsSectionBuilder.CreateScrollableRoot(rootElement);

            // ── Sidekick Account (with Pro license status) ────────────────────────
            // Account sits at the top of the page. License is a function of the account:
            // both sign-in and Refresh write the same entitlement token into the cache that
            // LicenseService.GetStatus() reads. License acquisition lives on the web
            // (ryx-sidekick.pro) — the editor only displays the cached license and re-pulls it
            // through account credentials (no license-key entry, no manual install button;
            // install/update is handled by the in-window paywall + status-bar chip).
            var accountSection = SidekickSettingsSectionBuilder.Section("Sidekick Account");
            root.Add(accountSection);

            var manager = Infrastructure.Auth.SidekickAccountManager.Instance;

            // LicenseService is built for GetStatus() only — there is no in-editor key activation.
            var verifier = new Infrastructure.Entitlements.RsaEntitlementVerifier(
                Infrastructure.Entitlements.RsaEntitlementVerifier.Base64UrlDecode(
                    string.IsNullOrEmpty(Infrastructure.Entitlements.SidekickEntitlementKey.PublicKeyN)
                        ? "AQAB" : Infrastructure.Entitlements.SidekickEntitlementKey.PublicKeyN),
                Infrastructure.Entitlements.RsaEntitlementVerifier.Base64UrlDecode(
                    string.IsNullOrEmpty(Infrastructure.Entitlements.SidekickEntitlementKey.PublicKeyE)
                        ? "AQAB" : Infrastructure.Entitlements.SidekickEntitlementKey.PublicKeyE));

            var creds = Infrastructure.Auth.CredentialStoreFactory.Create();
            var http = new Infrastructure.Net.UnityWebRequestHttpClient();
            var cache = new Infrastructure.Licensing.SettingsEntitlementCache();
            var machine = new Infrastructure.Licensing.SystemInfoMachineIdProvider();
            var clock = new Infrastructure.SystemClock();

            const string fnBase = "https://europe-west1-ryx-sidekick.cloudfunctions.net";
            var license = new UseCases.Licensing.LicenseService(
                http, verifier, creds, cache, machine, clock, fnBase + "/validateLicense");

            var accountStatusLabel = new Label();
            void RefreshAccountStatus()
            {
                var st = manager.GetStatus();
                accountStatusLabel.text = st.IsSignedIn
                    ? (string.IsNullOrEmpty(st.Profile?.Email)
                        ? $"Signed in · {st.Profile?.Plan ?? string.Empty}"
                        : $"Signed in: {st.Profile.Email} · {st.Profile?.Plan ?? string.Empty}")
                    : "Not signed in";
            }

            var licenseStatusLabel = new Label();

            // "Manage account" opens the modal via the Presentation bridge.
            var manageAccountBtn = new Button(SidekickSettingsModalHost.RequestAccountModal)
            {
                text = "Manage account"
            };

            // "Refresh" silently re-pulls the session via the stored refresh token (no
            // logout/login). PersistSession() rewrites the cached entitlement token, so the
            // License line updates too.
            var refreshBtn = new Button { text = "Refresh" };

            void RefreshAll()
            {
                RefreshAccountStatus();
                RefreshLicenseStatus();
                refreshBtn.SetEnabled(manager.GetStatus().IsSignedIn);
            }

            refreshBtn.clicked += () =>
            {
                refreshBtn.SetEnabled(false);
                licenseStatusLabel.text = "Refreshing…";
                _ = manager.RefreshAsync().ContinueWith(_ =>
                {
                    EditorApplication.delayCall += RefreshAll;
                });
            };

            void OnAccountStatusChanged(Domain.Account.SidekickAccountStatus _)
            {
                EditorApplication.delayCall += RefreshAll;
            }
            manager.OnStatusChanged += OnAccountStatusChanged;

            RefreshAll();

            accountSection.Add(SidekickSettingsSectionBuilder.FieldRow("Account", accountStatusLabel, null));
            accountSection.Add(SidekickSettingsSectionBuilder.FieldRow("License", licenseStatusLabel, null));
            var accountActions = SidekickSettingsSectionBuilder.HorizontalRow();
            accountActions.AddToClassList("sk-settings-actions");
            accountActions.style.marginTop = 8;
            accountActions.Add(manageAccountBtn);
            accountActions.Add(refreshBtn);
            accountSection.Add(accountActions);

            // Deactivate modal bridge and unsubscribe status listener when the settings page is removed.
            rootElement.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                manager.OnStatusChanged -= OnAccountStatusChanged;
                SidekickSettingsModalHost.NotifyDeactivated(rootElement);
            });

            // --- CLI section ---
            var cliSection = SidekickSettingsSectionBuilder.Section("CLI");
            root.Add(cliSection);

            var providers = CliProviderRegistry.AllProviders;
            var providerNames = providers.Select(p => p.DisplayName).ToList();
            var providerDropdown = new DropdownField(providerNames, ResolveActiveProviderIndex(settings, providers));
            providerDropdown.RegisterValueChangedCallback(evt =>
            {
                var index = providerNames.IndexOf(evt.newValue);
                if (index >= 0)
                {
                    settings.ProviderId = providers[index].Id;
                }
            });
            cliSection.Add(SidekickSettingsSectionBuilder.FieldRow("Provider", providerDropdown,
                "Active CLI provider. Configure each provider under Project/Sidekick/Providers."));

            var workingDir = new TextField { value = settings.WorkingDirectory };
            workingDir.RegisterValueChangedCallback(evt => settings.WorkingDirectory = evt.newValue);
            cliSection.Add(SidekickSettingsSectionBuilder.BrowseRow("Working Directory", workingDir, () =>
                EditorUtility.OpenFolderPanel("Select Working Directory", settings.WorkingDirectory, ""),
                "Working directory for the CLI (empty = project root)."));

            // --- Behavior section ---
            var behaviorSection = SidekickSettingsSectionBuilder.Section("Behavior");
            root.Add(behaviorSection);

            var refreshField = new EnumField(settings.AssetRefreshMode);
            refreshField.RegisterValueChangedCallback(evt => settings.AssetRefreshMode = (AssetRefreshMode)evt.newValue);
            behaviorSection.Add(SidekickSettingsSectionBuilder.FieldRow("Project Refresh", refreshField,
                "When to call AssetDatabase.Refresh() after assistant Edit/Write tools."));

            var maxTurns = new IntegerField { value = settings.MaxTurns };
            maxTurns.RegisterValueChangedCallback(evt => settings.MaxTurns = evt.newValue);
            behaviorSection.Add(SidekickSettingsSectionBuilder.FieldRow("Max Turns", maxTurns,
                "Maximum number of agentic turns."));

            var verbose = new Toggle { value = settings.VerboseLogging };
            verbose.RegisterValueChangedCallback(evt => settings.VerboseLogging = evt.newValue);
            behaviorSection.Add(SidekickSettingsSectionBuilder.FieldRow("Verbose Logging", verbose,
                "Enable verbose output from the CLI."));

            var agentHost = new Toggle { value = settings.UseAgentHost };
            agentHost.RegisterValueChangedCallback(evt => settings.UseAgentHost = evt.newValue);
            behaviorSection.Add(SidekickSettingsSectionBuilder.FieldRow("Use Agent Host (Experimental)", agentHost,
                "Run the CLI through an external Agent Host process that survives Unity domain reloads — " +
                "edits that trigger a recompile no longer kill the active turn or re-spend tokens on resume. " +
                "Falls back to the in-process host if the daemon is unavailable. Takes effect on the next turn."));

            // --- Debugging section ---
            var debuggingSection = SidekickSettingsSectionBuilder.Section("Debugging");
            root.Add(debuggingSection);

            var debugWarning = SidekickSettingsSectionBuilder.Help(
                "Debug mode is enabled. The CLI runs in a visible OS console window; streaming output will NOT appear in Unity.",
                HelpBoxMessageType.Warning);
            debugWarning.style.display = settings.DebugMode ? DisplayStyle.Flex : DisplayStyle.None;

            var debugMode = new Toggle { value = settings.DebugMode };
            debugMode.RegisterValueChangedCallback(evt =>
            {
                settings.DebugMode = evt.newValue;
                debugWarning.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
            debuggingSection.Add(SidekickSettingsSectionBuilder.FieldRow("Debug Mode", debugMode,
                "Run the CLI in a visible OS console for debugging."));
            debuggingSection.Add(debugWarning);

            // --- Validation section ---
            var validationSection = SidekickSettingsSectionBuilder.Section("Validation");
            root.Add(validationSection);

            var validationLabel = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            validationLabel.AddToClassList("sk-settings-help");
            validationLabel.style.display = DisplayStyle.None;

            var validateButton = new Button(() =>
            {
                var (success, message) = settings.ValidateCli();
                validationLabel.text = (success ? "✓  " : "✗  ") + message;
                validationLabel.style.color = success ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.9f, 0.3f, 0.3f);
                validationLabel.style.display = DisplayStyle.Flex;
            })
            {
                text = "Validate CLI"
            };
            validationSection.Add(validateButton);
            validationSection.Add(validationLabel);

            // --- Actions ---
            var actionsSection = SidekickSettingsSectionBuilder.Section("Actions");
            root.Add(actionsSection);
            var actions = SidekickSettingsSectionBuilder.HorizontalRow();
            actions.AddToClassList("sk-settings-actions");
            actions.style.marginTop = 8;
            actions.Add(new Button(() => EditorApplication.ExecuteMenuItem(SidekickAppConstants.MenuItems.WindowSidekick))
            {
                text = "Open Sidekick Window"
            });
            actions.Add(new Button(() =>
            {
                EditorPrefs.DeleteKey(OnboardingCompletedVersionKey);
                EditorApplication.ExecuteMenuItem(SidekickAppConstants.MenuItems.WindowSidekick);
            })
            {
                text = "Run Setup Wizard Again"
            });
            actionsSection.Add(actions);

            // Notify the Presentation bridge LAST so it can create a local modal layer for this
            // page. Done after all page content is added so the page content stays the first
            // child of rootElement (the modal layer's overlay root is absolute-positioned and
            // brought to front on Show, so its sibling order does not affect paint order).
            SidekickSettingsModalHost.NotifyActivated(rootElement);

            void RefreshLicenseStatus()
            {
                var st = license.GetStatus();
                if (st.State == UseCases.Licensing.LicenseState.Active)
                {
                    if (st is { EditionYear: > 0, SupportUntil: > 0 })
                    {
                        var windowEnd = DateTimeOffset.FromUnixTimeSeconds(st.SupportUntil).UtcDateTime;
                        licenseStatusLabel.text = $"{st.Sku} {st.EditionYear} · updates until {windowEnd:yyyy-MM-dd}";
                    }
                    else
                    {
                        licenseStatusLabel.text = $"Active ({st.Sku})";
                    }
                }
                else if (st.State == UseCases.Licensing.LicenseState.Expired)
                {
                    licenseStatusLabel.text = "Expired — Refresh to renew";
                }
                else
                {
                    licenseStatusLabel.text = manager.GetStatus().IsSignedIn
                        ? "No Pro license"
                        : "Sign in to sync your Pro license";
                }
            }
        }

        private static int ResolveActiveProviderIndex(SidekickSettings settings, System.Collections.Generic.IReadOnlyList<ICliProvider> providers)
        {
            for (var i = 0; i < providers.Count; i++)
            {
                if (string.Equals(providers[i].Id, settings.ProviderId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
