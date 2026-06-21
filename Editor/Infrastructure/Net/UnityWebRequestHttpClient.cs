// SPDX-License-Identifier: GPL-3.0-only
using System.Text;
using System.Threading.Tasks;
using Ryx.Sidekick.Editor.UseCases.Contracts;
using UnityEngine.Networking;

namespace Ryx.Sidekick.Editor.Infrastructure.Net
{
    internal sealed class UnityWebRequestHttpClient : IHttpClient
    {
        public Task<HttpGetResult> GetAsync(string url, int timeoutSeconds)
        {
            var tcs = new TaskCompletionSource<HttpGetResult>();
            var request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    var ok = request.result == UnityWebRequest.Result.Success;
                    tcs.SetResult(ok ? HttpGetResult.Success(request.downloadHandler.text)
                                     : HttpGetResult.Failure());
                }
                catch { tcs.SetResult(HttpGetResult.Failure()); }
                finally { request.Dispose(); }
            };
            return tcs.Task;
        }

        public Task<HttpResponse> PostJsonAsync(string url, string jsonBody, int timeoutSeconds)
            => PostJsonAsync(url, jsonBody, timeoutSeconds, null);

        public Task<HttpResponse> PostJsonAsync(string url, string jsonBody, int timeoutSeconds,
            System.Collections.Generic.IReadOnlyDictionary<string, string> headers)
        {
            var tcs = new TaskCompletionSource<HttpResponse>();
            var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody ?? string.Empty));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (headers != null)
            {
                foreach (var kv in headers)
                    request.SetRequestHeader(kv.Key, kv.Value);
            }
            request.timeout = timeoutSeconds;
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    long code = request.responseCode;
                    bool transportOk = request.result == UnityWebRequest.Result.Success
                                       || request.result == UnityWebRequest.Result.ProtocolError;
                    // Body is available on protocol errors (4xx) too.
                    string body = transportOk ? request.downloadHandler.text : null;
                    bool ok = code >= 200 && code < 300;
                    tcs.SetResult(new HttpResponse(ok, transportOk ? code : 0, body));
                }
                catch { tcs.SetResult(new HttpResponse(false, 0, null)); }
                finally { request.Dispose(); }
            };
            return tcs.Task;
        }
    }
}
