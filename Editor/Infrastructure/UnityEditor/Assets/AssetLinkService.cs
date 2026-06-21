// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ryx.Sidekick.Editor.Infrastructure.Assets
{
    /// <summary>
    /// Service for detecting, validating, and interacting with Unity asset paths in text.
    /// </summary>
    internal static class AssetLinkService
    {
        // Matches paths starting with Assets/ or Packages/ followed by valid path characters
        private static readonly Regex AssetPathRegex = new(
            @"(Assets|Packages)/[\w\-./]+\.\w+",
            RegexOptions.Compiled);
        
        private static string _projectPath;
        
        /// <summary>
        /// Converts an absolute path to a relative Unity asset path (Assets/... or Packages/...).
        /// Returns the original path if it's already relative or cannot be converted.
        /// </summary>
        public static string ToRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            
            path = path.Replace('\\', '/').Trim();
            
            // Already relative
            if (path.StartsWith("Assets/") || path.StartsWith("Packages/"))
                return path;
            
            // Get project path (cached)
            if (string.IsNullOrEmpty(_projectPath))
            {
                _projectPath = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
            }
            
            if (string.IsNullOrEmpty(_projectPath))
                return path;
            
            // Check if it's under project folder
            if (path.StartsWith(_projectPath + "/"))
            {
                return path.Substring(_projectPath.Length + 1);
            }
            
            // Try to extract Assets/ or Packages/ from absolute path
            var assetsIndex = path.IndexOf("/Assets/", StringComparison.Ordinal);
            if (assetsIndex >= 0)
            {
                return path.Substring(assetsIndex + 1);
            }
            
            var packagesIndex = path.IndexOf("/Packages/", StringComparison.Ordinal);
            if (packagesIndex >= 0)
            {
                return path.Substring(packagesIndex + 1);
            }
            
            return path;
        }

        /// <summary>
        /// Checks if the given path is a valid asset path in the project.
        /// </summary>
        public static bool IsValidAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Convert to relative path first
            path = ToRelativePath(path);

            // Must start with Assets/ or Packages/
            if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/"))
                return false;

            // Check if asset exists via GUID lookup (fast)
            var guid = AssetDatabase.AssetPathToGUID(path);
            return !string.IsNullOrEmpty(guid);
        }

        /// <summary>
        /// Pings the asset in the Project window and selects it.
        /// </summary>
        public static void PingAndSelect(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            path = ToRelativePath(path);
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            
            if (asset)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }

        /// <summary>
        /// Opens the asset in its default editor (script in IDE, prefab in scene, etc.).
        /// </summary>
        public static void OpenAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            path = ToRelativePath(path);
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);

            if (asset)
            {
                AssetDatabase.OpenAsset(asset);
            }
        }

        /// <summary>
        /// Opens the asset in its default editor at a specific line number.
        /// Useful for opening scripts at the location of changes.
        /// </summary>
        /// <param name="path">The asset path (absolute or relative)</param>
        /// <param name="lineNumber">The 1-based line number to open at</param>
        public static void OpenAssetAtLine(string path, int lineNumber)
        {
            if (string.IsNullOrEmpty(path))
                return;

            path = ToRelativePath(path);
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);

            if (asset)
            {
                AssetDatabase.OpenAsset(asset, lineNumber);
            }
        }

        /// <summary>
        /// Gets the cached icon for an asset at the given path.
        /// </summary>
        public static Texture GetAssetIcon(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            path = ToRelativePath(path);
            return AssetDatabase.GetCachedIcon(path);
        }

        /// <summary>
        /// Gets the file name without extension from an asset path.
        /// </summary>
        public static string GetAssetName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>
        /// Gets the file name with extension from an asset path.
        /// </summary>
        public static string GetAssetNameWithExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            return Path.GetFileName(path);
        }

        /// <summary>
        /// Finds all asset path matches in the given text.
        /// Returns a list of (startIndex, length, path) tuples for valid asset paths.
        /// </summary>
        public static List<AssetPathMatch> FindAssetPaths(string text)
        {
            var results = new List<AssetPathMatch>();
            
            if (string.IsNullOrEmpty(text))
                return results;

            var matches = AssetPathRegex.Matches(text);
            
            foreach (Match match in matches)
            {
                var path = match.Value;
                if (IsValidAssetPath(path))
                {
                    results.Add(new AssetPathMatch
                    {
                        StartIndex = match.Index,
                        Length = match.Length,
                        Path = path
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Represents a matched asset path in text.
        /// </summary>
        public struct AssetPathMatch
        {
            public int StartIndex;
            public int Length;
            public string Path;
        }
    }
}

