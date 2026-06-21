// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Abstraction over Unity-specific texture encoding so that Application-layer
    /// screenshot attachment workflows do not depend on UnityEngine directly.
    /// </summary>
    internal interface IViewScreenshotService
    {
        /// <summary>
        /// Encodes the given texture as a PNG byte array.
        /// Returns null if the texture is null or encoding fails.
        /// </summary>
        byte[] EncodePngFromTexture(Texture2D texture);
    }
}
