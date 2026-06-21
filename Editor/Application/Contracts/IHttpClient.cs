// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal readonly struct HttpGetResult
    {
        public readonly bool IsSuccess;
        public readonly string Body;
        public HttpGetResult(bool isSuccess, string body) { IsSuccess = isSuccess; Body = body; }
        public static HttpGetResult Success(string body) => new HttpGetResult(true, body);
        public static HttpGetResult Failure() => new HttpGetResult(false, null);
    }

    internal readonly struct HttpResponse
    {
        public readonly bool Ok;        // HTTP 2xx
        public readonly long Status;    // 0 = transport/network error
        public readonly string Body;    // response body if any (present on 4xx too)
        public HttpResponse(bool ok, long status, string body)
        {
            Ok = ok;
            Status = status;
            Body = body;
        }
    }

    internal interface IHttpClient
    {
        Task<HttpGetResult> GetAsync(string url, int timeoutSeconds);
        Task<HttpResponse> PostJsonAsync(string url, string jsonBody, int timeoutSeconds);
        Task<HttpResponse> PostJsonAsync(string url, string jsonBody, int timeoutSeconds,
            System.Collections.Generic.IReadOnlyDictionary<string, string> headers);
    }
}
