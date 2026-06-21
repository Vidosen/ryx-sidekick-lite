// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;

namespace Ryx.Sidekick.Editor
{
    /// <summary>
    /// Help menu items for Ryx Sidekick documentation.
    /// </summary>
    internal static class PluginHelpMenu
    {
        [MenuItem(SidekickAppConstants.MenuItems.HelpDocumentation, priority = 1000)]
        private static void OpenDocumentation()
        {
            OpenDocumentationWithTab();
        }

        [MenuItem(SidekickAppConstants.MenuItems.HelpChangelog, priority = 1001)]
        private static void OpenChangelog()
        {
            OpenDocumentationWithTab("changelog");
        }

        private static void OpenDocumentationWithTab(string tab = null)
        {
            var htmlPath = System.IO.Path.GetFullPath(SidekickUiConstants.DocumentationIndexPath);
            var changelogPath = System.IO.Path.GetFullPath(SidekickUiConstants.ChangelogPath);
            var logoPath = System.IO.Path.GetFullPath(SidekickUiConstants.LogoAssetPath);

            if (!System.IO.File.Exists(htmlPath))
            {
                Debug.LogWarning($"[Ryx Sidekick] Documentation not found at: {htmlPath}");
                return;
            }

            // Read changelog and inject it into HTML to avoid CORS issues with file:// URLs
            string changelogContent = "";
            if (System.IO.File.Exists(changelogPath))
            {
                changelogContent = System.IO.File.ReadAllText(changelogPath);
            }

            var htmlContent = System.IO.File.ReadAllText(htmlPath);

            // Escape changelog for JavaScript string embedding
            var escapedChangelog = changelogContent
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n");

            // Inject changelog as embedded data before the closing </head> tag
            var injectionScript = $"<script>window.EMBEDDED_CHANGELOG = '{escapedChangelog}';</script>\n</head>";
            htmlContent = htmlContent.Replace("</head>", injectionScript);

            // Embed logo as base64 to fix relative path issue in temp file
            if (System.IO.File.Exists(logoPath))
            {
                var logoBytes = System.IO.File.ReadAllBytes(logoPath);
                var logoBase64 = System.Convert.ToBase64String(logoBytes);
                var logoDataUrl = $"data:image/png;base64,{logoBase64}";
                htmlContent = htmlContent.Replace(SidekickUiConstants.DocumentationLogoRelativePath, logoDataUrl);
            }

            // Write to temp file and open
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), SidekickAppConstants.Files.DocsTempFileName);
            System.IO.File.WriteAllText(tempPath, htmlContent);

            var url = $"file://{tempPath}";
            if (!string.IsNullOrEmpty(tab))
            {
                url += $"#{tab}";
            }
            Application.OpenURL(url);
        }
    }
}
