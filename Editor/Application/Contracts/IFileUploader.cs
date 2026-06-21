// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    /// <summary>Uploads a local file to a pre-signed URL via HTTP PUT.</summary>
    internal interface IFileUploader
    {
        /// <summary>PUT <paramref name="filePath"/> to <paramref name="url"/>. Returns true on HTTP success.</summary>
        Task<bool> PutFileAsync(string url, string filePath, string contentType, int timeoutSeconds);
    }
}
