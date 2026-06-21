// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using Ryx.Sidekick.Editor.Constants;
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    internal sealed class UnityClipboardService : IClipboardService
    {
        // Mirrors SidekickUiConstants.ClipboardTempFilePrefix; the constant
        // lives in Presentation/Constants and is intentionally not imported
        // here because Sidekick.Editor.Infrastructure must not depend on
        // Sidekick.Editor.Presentation (would create an asmdef cycle).
        private const string ClipboardTempFilePrefix = "sidekick-clipboard";


        /// <summary>
        /// Returns the system clipboard text.
        /// Uses EditorGUIUtility.systemCopyBuffer (the editor-specific accessor)
        /// which is what AttachmentController historically read.
        /// In the Unity Editor both GUIUtility and EditorGUIUtility route to the
        /// same underlying buffer; prefer EditorGUIUtility here for clarity.
        /// </summary>
        public string Text
        {
            get => EditorGUIUtility.systemCopyBuffer;
            set => EditorGUIUtility.systemCopyBuffer = value;
        }

        /// <summary>
        /// Attempts to extract a PNG image from the system clipboard.
        /// On macOS this shells out to osascript; on Windows to PowerShell.
        /// Returns false when the clipboard holds no image or capture fails.
        /// </summary>
        public bool TryReadImagePng(out byte[] pngBytes)
        {
            pngBytes = null;

#if UNITY_EDITOR_OSX
            return TryReadImagePngMac(out pngBytes);
#elif UNITY_EDITOR_WIN
            return TryReadImagePngWindows(out pngBytes);
#else
            return false;
#endif
        }

#if UNITY_EDITOR_OSX
        private static bool TryReadImagePngMac(out byte[] pngBytes)
        {
            pngBytes = null;

            var tempDir = Path.GetTempPath();
            var guid = Guid.NewGuid().ToString("N");
            var pngPath = Path.Combine(tempDir, $"{ClipboardTempFilePrefix}-{guid}.png");
            var tiffPath = Path.Combine(tempDir, $"{ClipboardTempFilePrefix}-{guid}.tiff");
            var scriptPath = Path.Combine(tempDir, $"{ClipboardTempFilePrefix}-{guid}.applescript");

            try
            {
                var script = $@"
on writeData(imgData, outPath)
  set outFile to POSIX file outPath
  set fileRef to open for access outFile with write permission
  set eof fileRef to 0
  write imgData to fileRef
  close access fileRef
end writeData

on run argv
  set pngPath to item 1 of argv
  set tiffPath to item 2 of argv
  try
    set imgData to the clipboard as «class PNGf»
    my writeData(imgData, pngPath)
    return ""PNG""
  on error
    try
      set imgData to the clipboard as «class TIFF»
      my writeData(imgData, tiffPath)
      return ""TIFF""
    on error errMsg
      return ""ERROR:"" & errMsg
    end try
  end try
end run
";

                File.WriteAllText(scriptPath, script);

                var result = AttachmentUtils.RunProcess("/usr/bin/osascript",
                    $"{AttachmentUtils.Quote(scriptPath)} {AttachmentUtils.Quote(pngPath)} {AttachmentUtils.Quote(tiffPath)}",
                    out var stdout,
                    out var stderr);

                if (result != 0)
                {
                    if (SidekickSettings.instance.VerboseLogging)
                    {
                        Debug.LogWarning($"[Ryx Sidekick] osascript failed: {stderr}");
                    }
                    return false;
                }

                var mode = (stdout ?? "").Trim();
                if (mode.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(mode, "TIFF", StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists(tiffPath) || new FileInfo(tiffPath).Length == 0)
                    {
                        return false;
                    }

                    var sips = AttachmentUtils.RunProcess("/usr/bin/sips",
                        $"-s format png {AttachmentUtils.Quote(tiffPath)} --out {AttachmentUtils.Quote(pngPath)}",
                        out _,
                        out _);
                    if (sips != 0)
                    {
                        return false;
                    }
                }

                if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
                {
                    return false;
                }

                pngBytes = File.ReadAllBytes(pngPath);
                return true;
            }
            catch (Exception ex)
            {
                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.LogWarning($"[Ryx Sidekick] Failed to paste image from macOS clipboard: {ex.Message}");
                }
                return false;
            }
            finally
            {
                AttachmentUtils.TryDeleteFile(scriptPath);
                AttachmentUtils.TryDeleteFile(pngPath);
                AttachmentUtils.TryDeleteFile(tiffPath);
            }
        }
#endif

#if UNITY_EDITOR_WIN
        private static bool TryReadImagePngWindows(out byte[] pngBytes)
        {
            pngBytes = null;

            var tempDir = Path.GetTempPath();
            var guid = Guid.NewGuid().ToString("N");
            var pngPath = Path.Combine(tempDir, $"{ClipboardTempFilePrefix}-{guid}.png");
            var scriptPath = Path.Combine(tempDir, $"{ClipboardTempFilePrefix}-{guid}.ps1");

            try
            {
                var script = @"
param([string]$outPath)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$img = [System.Windows.Forms.Clipboard]::GetImage()
if ($null -eq $img) { exit 2 }
$img.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
exit 0
";

                File.WriteAllText(scriptPath, script);

                var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {AttachmentUtils.Quote(scriptPath)} {AttachmentUtils.Quote(pngPath)}";
                var code = AttachmentUtils.RunProcess("powershell", args, out _, out var stderr, timeoutMs: 8000);
                if (code != 0)
                {
                    if (SidekickSettings.instance.VerboseLogging)
                    {
                        Debug.LogWarning($"[Ryx Sidekick] PowerShell clipboard export failed (code {code}): {stderr}");
                    }
                    return false;
                }

                if (!File.Exists(pngPath) || new FileInfo(pngPath).Length == 0)
                {
                    return false;
                }

                pngBytes = File.ReadAllBytes(pngPath);
                return true;
            }
            catch (Exception ex)
            {
                if (SidekickSettings.instance.VerboseLogging)
                {
                    Debug.LogWarning($"[Ryx Sidekick] Failed to paste image from Windows clipboard: {ex.Message}");
                }
                return false;
            }
            finally
            {
                AttachmentUtils.TryDeleteFile(scriptPath);
                AttachmentUtils.TryDeleteFile(pngPath);
            }
        }
#endif
    }
}
