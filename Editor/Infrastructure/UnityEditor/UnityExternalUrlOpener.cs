// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.UnityEditor
{
    internal sealed class UnityExternalUrlOpener : IExternalUrlOpener
    {
        public void Open(string url)
        {
            if (!string.IsNullOrWhiteSpace(url)) UnityEngine.Application.OpenURL(url);
        }
    }
}
