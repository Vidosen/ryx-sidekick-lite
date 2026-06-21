// SPDX-License-Identifier: GPL-3.0-only
using System.Threading.Tasks;

namespace Ryx.Sidekick.Editor.UseCases.Contracts
{
    internal interface IFileDownloader
    {
        /// <summary>Stream a URL to a local file. Returns true on HTTP success.</summary>
        Task<bool> DownloadToFileAsync(string url, string destinationPath, int timeoutSeconds);
    }
}
