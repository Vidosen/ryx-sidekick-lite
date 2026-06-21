// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Ryx.Sidekick.Editor.Infrastructure.Net
{
    internal sealed class UnityFileDownloader : Ryx.Sidekick.Editor.UseCases.Contracts.IFileDownloader
    {
        public Task<bool> DownloadToFileAsync(string url, string destinationPath, int timeoutSeconds)
        {
            var tcs = new TaskCompletionSource<bool>();
            var request = new UnityWebRequest(url, "GET");
            request.downloadHandler = new DownloadHandlerFile(destinationPath) { removeFileOnAbort = true };
            request.timeout = timeoutSeconds;
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                try { tcs.SetResult(request.result == UnityWebRequest.Result.Success); }
                catch { tcs.SetResult(false); }
                finally { request.Dispose(); }
            };
            return tcs.Task;
        }
    }
}
