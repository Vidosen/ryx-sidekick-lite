// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEngine.Networking;

namespace Ryx.Sidekick.Editor.Infrastructure.Net
{
    internal sealed class UnityFileUploader : IFileUploader
    {
        public Task<bool> PutFileAsync(string url, string filePath, string contentType, int timeoutSeconds)
        {
            var tcs = new TaskCompletionSource<bool>();
            var request = new UnityWebRequest(url, "PUT");
            request.uploadHandler = new UploadHandlerRaw(File.ReadAllBytes(filePath));
            request.uploadHandler.contentType = contentType;
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", contentType);
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
