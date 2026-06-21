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

            // ── License (Pro activation via key-fallback) ──────────────────────
            var licenseSection = SidekickSettingsSectionBuilder.Section("License");

            var verifier = new Ryx.Sidekick.Editor.Infrastructure.Entitlements.RsaEntitlementVerifier(
                Ryx.Sidekick.Editor.Infrastructure.Entitlements.RsaEntitlementVerifier.Base64UrlDecode(
                    string.IsNullOrEmpty(Ryx.Sidekick.Editor.Infrastructure.Entitlements.SidekickEntitlementKey.PublicKeyN)
                        ? "AQAB" : Ryx.Sidekick.Editor.Infrastructure.Entitlements.SidekickEntitlementKey.PublicKeyN),
                Ryx.Sidekick.Editor.Infrastructure.Entitlements.RsaEntitlementVerifier.Base64UrlDecode(
                    string.IsNullOrEmpty(Ryx.Sidekick.Editor.Infrastructure.Entitlements.SidekickEntitlementKey.PublicKeyE)
                        ? "AQAB" : Ryx.Sidekick.Editor.Infrastructure.Entitlements.SidekickEntitlementKey.PublicKeyE));

            var creds = Ryx.Sidekick.Editor.Infrastructure.Auth.CredentialStoreFactory.Create();
            var http = new Ryx.Sidekick.Editor.Infrastructure.Net.UnityWebRequestHttpClient();
            var cache = new Ryx.Sidekick.Editor.Infrastructure.Licensing.SettingsEntitlementCache();
            var machine = new Ryx.Sidekick.Editor.Infrastructure.Licensing.SystemInfoMachineIdProvider();
            var clock = new Ryx.Sidekick.Editor.Infrastructure.SystemClock();

            const string fnBase = "https://europe-west1-ryx-sidekick.cloudfunctions.net";
            var license = new Ryx.Sidekick.Editor.UseCases.Licensing.LicenseService(
                http, verifier, creds, cache, machine, clock, fnBase + "/validateLicense");

            var statusLabel = new UnityEngine.UIElements.Label();
            void RefreshStatus()
            {
                var st = license.GetStatus();
                if (st.State == Ryx.Sidekick.Editor.UseCases.Licensing.LicenseState.Active)
                {
                    if (st.EditionYear > 0 && st.SupportUntil > 0)
                    {
                        var windowEnd = DateTimeOffset.FromUnixTimeSeconds(st.SupportUntil).UtcDateTime;
                        statusLabel.text = $"{st.Sku} {st.EditionYear} · updates until {windowEnd:yyyy-MM-dd}";
                    }
                    else
                    {
                        statusLabel.text = $"Active ({st.Sku})";
                    }
                }
                else if (st.State == Ryx.Sidekick.Editor.UseCases.Licensing.LicenseState.Expired)
                {
                    statusLabel.text = "Expired — reconnect to refresh";
                }
                else
                {
                    statusLabel.text = "Not activated";
                }
            }
            RefreshStatus();

            var keyField = new UnityEngine.UIElements.TextField { isPasswordField = true };
            var activateBtn = new UnityEngine.UIElements.Button { text = "Activate" };
            activateBtn.clicked += () =>
            {
                activateBtn.SetEnabled(false);
                statusLabel.text = "Activating…";
                _ = license.ActivateAsync(keyField.value,
                        UnityEngine.Application.unityVersion, UnityEngine.Application.platform.ToString())
                    .ContinueWith(t =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            activateBtn.SetEnabled(true);
                            if (t.IsCompletedSuccessfully &&
                                t.Result.Outcome == Ryx.Sidekick.Editor.UseCases.Licensing.LicenseActivationOutcome.Success)
                                RefreshStatus();
                            else
                                statusLabel.text = "Activation failed: " +
                                    (t.IsCompletedSuccessfully ? t.Result.Outcome.ToString() : "error");
                        };
                    });
            };

            var installer = new Ryx.Sidekick.Editor.Infrastructure.Licensing.EditorPackageInstaller();
            var downloader = new Ryx.Sidekick.Editor.Infrastructure.Net.UnityFileDownloader();
            var update = new Ryx.Sidekick.Editor.UseCases.Licensing.UpdateService(
                http, downloader, installer, fnBase + "/getDownloadUrl",
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ryx-sidekick-update"));

            var updateBtn = new UnityEngine.UIElements.Button { text = "Download & install latest Pro" };
            updateBtn.clicked += () =>
            {
                var token = cache.Read();
                if (string.IsNullOrEmpty(token)) { statusLabel.text = "Activate a license first."; return; }

                // Route version selection through EntitledReleaseResolver: download the newest
                // release whose releaseDate is within the support window (releaseDate <= supportUntil).
                // The server stays the authority — out-of-window downloads are rejected with
                // `update_window_expired` even if the client picks wrong.
                var supportUntil = license.GetStatus().SupportUntil;
                var remote = new Ryx.Sidekick.Editor.Infrastructure.Pro.RemoteConfigSource(
                    http, new Ryx.Sidekick.Editor.Infrastructure.Pro.RemoteConfigCache(), null);
                updateBtn.SetEnabled(false);
                statusLabel.text = "Checking…";
                _ = remote.RefreshAsync().ContinueWith(_ =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        var pro = remote.Current?.Releases?.Pro;
                        var entitled = Ryx.Sidekick.Editor.UseCases.Licensing.EntitledReleaseResolver.Resolve(
                            pro?.Versions, supportUntil);

                        string version;
                        string suffix = "";
                        if (!string.IsNullOrEmpty(entitled.Version))
                        {
                            // Newest entitled release inside the support window.
                            version = entitled.Version;
                            if (entitled.HasNewerOutOfWindow)
                                suffix = " (newer version available — renew to update)";
                        }
                        else if (pro?.Versions != null && pro.Versions.Count > 0)
                        {
                            // Version metadata exists but nothing falls in the window → window ended.
                            updateBtn.SetEnabled(true);
                            statusLabel.text = "Update window ended — renew to get newer versions.";
                            return;
                        }
                        else
                        {
                            // Old config without versions[] → fall back to `latest`; server still enforces the window.
                            version = pro?.Latest;
                        }

                        if (string.IsNullOrEmpty(version))
                        { updateBtn.SetEnabled(true); statusLabel.text = "No Pro release info."; return; }

                        statusLabel.text = $"Downloading Pro {version}…";
                        _ = update.DownloadAndInstallAsync("pro", version,
                                new[] { "com.ryxinteractive.sidekick.pro" }, token)
                            .ContinueWith(t =>
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    updateBtn.SetEnabled(true);
                                    statusLabel.text = t.IsCompletedSuccessfully
                                        ? "Update: " + t.Result + suffix
                                        : "Update failed";
                                };
                            });
                    };
                });
            };

            licenseSection.Add(SidekickSettingsSectionBuilder.FieldRow("License key", keyField,
                "Offline-fallback Pro license key (only its hash is sent)."));
            licenseSection.Add(SidekickSettingsSectionBuilder.FieldRow("Status", statusLabel, null));
            licenseSection.Add(activateBtn);
            licenseSection.Add(updateBtn);
            root.Add(licenseSection);

            // ── Sidekick Account ──────────────────────────────────────────────────
            var accountSection = SidekickSettingsSectionBuilder.Section("Sidekick Account");

            var manager = Ryx.Sidekick.Editor.Infrastructure.Auth.SidekickAccountManager.Instance;

            var accountStatusLabel = new UnityEngine.UIElements.Label();
            var accountSignInBtn = new UnityEngine.UIElements.Button { text = "Sign in" };
            var accountSignOutBtn = new UnityEngine.UIElements.Button { text = "Sign out" };

            void RefreshAccountStatus()
            {
                var st = manager.GetStatus();
                if (st.IsSignedIn)
                {
                    var email = st.Profile?.Email ?? string.Empty;
                    var plan = st.Profile?.Plan ?? string.Empty;
                    accountStatusLabel.text = !string.IsNullOrEmpty(email)
                        ? $"Signed in: {email} · {plan}"
                        : $"Signed in · {plan}";
                }
                else
                {
                    accountStatusLabel.text = "Not signed in";
                }
                // Show only the relevant button for the current state.
                accountSignInBtn.style.display = st.IsSignedIn ? DisplayStyle.None : DisplayStyle.Flex;
                accountSignOutBtn.style.display = st.IsSignedIn ? DisplayStyle.Flex : DisplayStyle.None;
            }
            RefreshAccountStatus();

            void OnAccountStatusChanged(Ryx.Sidekick.Editor.Domain.Account.SidekickAccountStatus _)
            {
                EditorApplication.delayCall += RefreshAccountStatus;
            }
            manager.OnStatusChanged += OnAccountStatusChanged;

            accountSignInBtn.clicked += () =>
            {
                accountSignInBtn.SetEnabled(false);
                accountStatusLabel.text = "Opening browser…";
                _ = manager.StartLoginAsync(url => UnityEngine.Application.OpenURL(url))
                    .ContinueWith(_ =>
                    {
                        EditorApplication.delayCall += () =>
                        {
                            accountSignInBtn.SetEnabled(true);
                            RefreshAccountStatus();
                        };
                    });
            };

            accountSignOutBtn.clicked += () =>
            {
                _ = manager.SignOutAsync().ContinueWith(_ =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        RefreshAccountStatus();
                    };
                });
            };

            accountSection.Add(SidekickSettingsSectionBuilder.FieldRow("Status", accountStatusLabel, null));
            var accountButtonRow = SidekickSettingsSectionBuilder.HorizontalRow();
            accountButtonRow.Add(accountSignInBtn);
            accountButtonRow.Add(accountSignOutBtn);
            accountSection.Add(accountButtonRow);
            root.Add(accountSection);

            // Unsubscribe from status changed when the settings page is disposed
            rootElement.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                manager.OnStatusChanged -= OnAccountStatusChanged;
            });

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
        }

        private static int ResolveActiveProviderIndex(SidekickSettings settings, System.Collections.Generic.IReadOnlyList<ICliProvider> providers)
        {
            for (var i = 0; i < providers.Count; i++)
            {
                if (string.Equals(providers[i].Id, settings.ProviderId, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
