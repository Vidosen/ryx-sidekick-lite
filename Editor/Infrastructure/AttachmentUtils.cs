// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;

namespace Ryx.Sidekick.Editor
{
    internal static class AttachmentUtils
    {
        public static string GetDefaultFileNameForMediaType(string mediaType)
        {
            if (string.IsNullOrEmpty(mediaType)) return "clipboard.png";

            if (mediaType.IndexOf("jpeg", StringComparison.OrdinalIgnoreCase) >= 0) return "clipboard.jpg";
            if (mediaType.IndexOf("jpg", StringComparison.OrdinalIgnoreCase) >= 0) return "clipboard.jpg";
            if (mediaType.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0) return "clipboard.png";
            if (mediaType.IndexOf("gif", StringComparison.OrdinalIgnoreCase) >= 0) return "clipboard.gif";
            if (mediaType.IndexOf("webp", StringComparison.OrdinalIgnoreCase) >= 0) return "clipboard.webp";

            return "clipboard.png";
        }

        public static string GetMediaTypeFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => null
            };
        }

        public static int RunProcess(string fileName, string arguments, out string stdout, out string stderr, int timeoutMs = 5000)
        {
            stdout = "";
            stderr = "";

            try
            {
                using var proc = new Process();
                proc.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                proc.Start();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { /* ignore */ }
                    return -1;
                }

                stdout = proc.StandardOutput.ReadToEnd();
                stderr = proc.StandardError.ReadToEnd();
                return proc.ExitCode;
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return -1;
            }
        }

        public static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        public static void TryDeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}


