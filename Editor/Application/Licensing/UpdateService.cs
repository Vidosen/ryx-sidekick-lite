// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.UseCases.Licensing
{
    internal enum UpdateOutcome { Staged, UrlError, DownloadError }

    internal sealed class UpdateService
    {
        private const int TimeoutSeconds = 60;
        private readonly IHttpClient _http;
        private readonly IFileDownloader _downloader;
        private readonly IPackageInstaller _installer;
        private readonly string _getDownloadUrl;
        private readonly string _downloadDir;

        public UpdateService(IHttpClient http, IFileDownloader downloader, IPackageInstaller installer,
            string getDownloadUrl, string downloadDir)
        {
            _http = http; _downloader = downloader; _installer = installer;
            _getDownloadUrl = getDownloadUrl; _downloadDir = downloadDir;
        }

        public async Task<UpdateOutcome> DownloadAndInstallAsync(
            string sku, string version, string[] packages, string entitlementToken)
        {
            if (string.IsNullOrEmpty(sku) || string.IsNullOrEmpty(version) || string.IsNullOrEmpty(entitlementToken))
                return UpdateOutcome.UrlError;

            var body = new JObject
            {
                ["entitlementToken"] = entitlementToken,
                ["sku"] = sku,
                ["version"] = version,
            }.ToString(Newtonsoft.Json.Formatting.None);

            HttpResponse resp;
            try { resp = await _http.PostJsonAsync(_getDownloadUrl, body, TimeoutSeconds); }
            catch { return UpdateOutcome.UrlError; }

            if (!resp.Ok || string.IsNullOrEmpty(resp.Body)) return UpdateOutcome.UrlError;

            string url = null;
            try { url = JObject.Parse(resp.Body)["url"]?.ToString(); } catch { }
            if (string.IsNullOrEmpty(url)) return UpdateOutcome.UrlError;

            Directory.CreateDirectory(_downloadDir);
            var dest = Path.Combine(_downloadDir, "payload.unitypackage");

            bool ok;
            try { ok = await _downloader.DownloadToFileAsync(url, dest, TimeoutSeconds); }
            catch { ok = false; }
            if (!ok) return UpdateOutcome.DownloadError;

            _installer.StageUpdate(sku, version, packages, dest);
            return UpdateOutcome.Staged;
        }
    }
}
