// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    /// <summary>
    /// Infrastructure adapter wrapping Unity-specific texture encoding for
    /// screenshot attachment workflows.
    /// </summary>
    internal sealed class UnityViewScreenshotService : IViewScreenshotService
    {
        /// <summary>
        /// Encodes the given texture as a PNG byte array using Texture2D.EncodeToPNG().
        /// Returns null if the texture is null or encoding fails.
        /// </summary>
        public byte[] EncodePngFromTexture(Texture2D texture)
        {
            if (texture == null) return null;
            try
            {
                return texture.EncodeToPNG();
            }
            catch
            {
                return null;
            }
        }
    }
}
