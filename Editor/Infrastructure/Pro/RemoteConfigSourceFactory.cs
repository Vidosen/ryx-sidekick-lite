// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Infrastructure.Net;
using Ryx.Sidekick.Editor.UseCases.Contracts;

namespace Ryx.Sidekick.Editor.Infrastructure.Pro
{
    /// <summary>
    /// Builds the standard file-cached + baked-fallback <see cref="RemoteConfigSource"/> used by
    /// settings-page consumers (MCP recommendations, in-Settings paywall). Centralizes the wiring so
    /// the baked offline fallback (<see cref="BakedConfigSource"/>) is never accidentally omitted.
    /// NOTE: composing touches the file cache + baked Resources, so call lazily on first use —
    /// never at <c>[InitializeOnLoad]</c> time.
    /// </summary>
    internal static class RemoteConfigSourceFactory
    {
        internal static IRemoteConfigSource Create() =>
            new RemoteConfigSource(new UnityWebRequestHttpClient(), new RemoteConfigCache(), new BakedConfigSource());
    }
}
