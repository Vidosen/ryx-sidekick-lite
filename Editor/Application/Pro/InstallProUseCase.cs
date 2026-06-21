// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using Ryx.Sidekick.Editor.UseCases.Licensing;

namespace Ryx.Sidekick.Editor.UseCases.Pro
{
    internal enum InstallProOutcome
    {
        /// <summary>Payload imported — the editor will reload to activate Pro.</summary>
        Staged,
        /// <summary>No cached entitlement token — the user must sign in / activate first.</summary>
        NeedsActivation,
        /// <summary>The support window ended; no in-window release to install.</summary>
        WindowEnded,
        /// <summary>Remote config carried no usable Pro release/version.</summary>
        NoReleaseInfo,
        /// <summary>Download or signed-URL request failed.</summary>
        Failed
    }

    internal readonly struct InstallProResult
    {
        public readonly InstallProOutcome Outcome;
        public readonly string Message;
        public InstallProResult(InstallProOutcome outcome, string message)
        {
            Outcome = outcome;
            Message = message;
        }
    }

    /// <summary>
    /// One-click "install Sidekick Pro" for an entitled user. Wraps the same flow exposed in
    /// Project Settings → License: resolve the newest in-window release, request a signed download
    /// URL with the cached entitlement token, download the payload, and import it via the two-stage
    /// installer (which triggers a domain reload that activates Pro). Server stays the authority on
    /// the support window — out-of-window downloads are rejected even if the client picks wrong.
    /// </summary>
    internal interface IProInstaller
    {
        Task<InstallProResult> InstallLatestAsync(Action<string> onStatus = null);
    }

    /// <inheritdoc cref="IProInstaller"/>
    internal sealed class InstallProUseCase : IProInstaller
    {
        // Same Cloud Functions base + endpoint the Project Settings install flow uses.
        private const string FunctionsBase = "https://europe-west1-ryx-sidekick.cloudfunctions.net";
        private const string GetDownloadUrl = FunctionsBase + "/getDownloadUrl";

        // Package ids the two-stage installer reconciles for a Pro install (matches the manifest the
        // server payload carries). Mirrors SidekickGeneralSettingsProvider's "Download & install" call.
        private static readonly string[] ProPackages = { "com.ryxinteractive.sidekick.pro" };

        private readonly IHttpClient _http;
        private readonly IFileDownloader _downloader;
        private readonly IPackageInstaller _installer;
        private readonly IEntitlementCache _cache;
        private readonly IRemoteConfigSource _remote;
        private readonly IProEntitlement _entitlement;

        public InstallProUseCase(
            IHttpClient http,
            IFileDownloader downloader,
            IPackageInstaller installer,
            IEntitlementCache cache,
            IRemoteConfigSource remote,
            IProEntitlement entitlement)
        {
            _http = http;
            _downloader = downloader;
            _installer = installer;
            _cache = cache;
            _remote = remote;
            _entitlement = entitlement;
        }

        public async Task<InstallProResult> InstallLatestAsync(Action<string> onStatus = null)
        {
            var token = _cache?.Read();
            if (string.IsNullOrEmpty(token))
                return new InstallProResult(InstallProOutcome.NeedsActivation, "Sign in or activate a license to install Pro.");

            onStatus?.Invoke("Checking for the latest Pro release…");

            // Refresh remote config so version selection sees the newest releases[] metadata.
            if (_remote != null)
            {
                try { await _remote.RefreshAsync(); } catch { /* fall back to cached/baked config below */ }
            }

            var pro = _remote?.Current?.Releases?.Pro;
            var supportUntil = _entitlement?.Get().SupportUntil ?? 0L;
            var entitled = EntitledReleaseResolver.Resolve(pro?.Versions, supportUntil);

            string version;
            string suffix = string.Empty;
            if (!string.IsNullOrEmpty(entitled.Version))
            {
                version = entitled.Version;
                if (entitled.HasNewerOutOfWindow)
                    suffix = " (newer version available — renew to update)";
            }
            else if (pro?.Versions != null && pro.Versions.Count > 0)
            {
                // Version metadata exists but nothing falls in the window → the window ended.
                return new InstallProResult(InstallProOutcome.WindowEnded,
                    "Update window ended — renew to get newer versions.");
            }
            else
            {
                // Old config without versions[] → fall back to `latest`; the server still enforces the window.
                version = pro?.Latest;
            }

            if (string.IsNullOrEmpty(version))
                return new InstallProResult(InstallProOutcome.NoReleaseInfo, "No Pro release information available.");

            onStatus?.Invoke($"Downloading Pro {version}…");

            var update = new UpdateService(_http, _downloader, _installer, GetDownloadUrl, DownloadDirectory);
            UpdateOutcome outcome;
            try { outcome = await update.DownloadAndInstallAsync("pro", version, ProPackages, token); }
            catch { outcome = UpdateOutcome.UrlError; }

            switch (outcome)
            {
                case UpdateOutcome.Staged:
                    return new InstallProResult(InstallProOutcome.Staged,
                        "Installing Pro — the editor will reload to finish." + suffix);
                case UpdateOutcome.DownloadError:
                    return new InstallProResult(InstallProOutcome.Failed, "Download failed. Check your connection and try again.");
                default:
                    return new InstallProResult(InstallProOutcome.Failed, "Couldn't reach the download service. Try again later.");
            }
        }

        private static string DownloadDirectory =>
            Path.Combine(Path.GetTempPath(), "ryx-sidekick-update");
    }
}
