// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor
{
    internal interface IClipboardService
    {
        string Text { get; set; }

        /// <summary>
        /// Attempts to read an image from the system clipboard as raw PNG bytes.
        /// On macOS this shells out to osascript to extract the native clipboard image payload.
        /// On Windows this shells out to PowerShell.
        /// Returns false when the clipboard holds no image data or capture fails.
        /// </summary>
        bool TryReadImagePng(out byte[] pngBytes);
    }
}
