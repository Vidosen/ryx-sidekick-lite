// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ryx.Sidekick.Editor.Domain.Models;
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure.Assets
{
    /// <summary>
    /// Service for creating context attachments from Unity assets and GameObjects.
    /// </summary>
    internal static class ContextAttachmentService
    {
        public const int MaxFileSizeBytes = 100 * 1024; // 100KB
        public const int TruncationHeadBytes = 50 * 1024; // First 50KB
        public const int TruncationTailBytes = 10 * 1024; // Last 10KB

        private static readonly HashSet<string> TextFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".json", ".xml", ".txt", ".shader", ".uxml", ".uss",
            ".asmdef", ".md", ".yml", ".yaml", ".html", ".css", ".js",
            ".cginc", ".hlsl", ".glsl", ".compute", ".raytrace",
            ".inputactions", ".preset", ".asset", ".controller", ".anim",
            ".unity" // Scene files are text
        };

        /// <summary>
        /// Creates a FileContextAttachment from an asset path.
        /// Returns null if the file is binary, too large, or doesn't exist.
        /// </summary>
        public static FileContextAttachment CreateFromAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            // Convert to relative path if needed
            var relativePath = AssetLinkService.ToRelativePath(assetPath);
            if (string.IsNullOrEmpty(relativePath))
                return null;

            // Check if it's a text file
            if (!IsTextFile(relativePath))
                return null;

            // Get full path
            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
            if (!File.Exists(fullPath))
                return null;

            // Check if binary
            if (IsBinaryFile(fullPath))
                return null;

            var fileInfo = new FileInfo(fullPath);
            var content = ReadFileWithTruncation(fullPath, fileInfo.Length);

            return new FileContextAttachment
            {
                Id = Guid.NewGuid().ToString("N"),
                FilePath = relativePath,
                Content = content.Text,
                IsTruncated = content.WasTruncated,
                OriginalSize = fileInfo.Length
            };
        }

        /// <summary>
        /// Creates a GameObjectContextAttachment from a GameObject.
        /// Works for both scene objects and prefab assets.
        /// </summary>
        public static GameObjectContextAttachment CreateFromGameObject(GameObject go)
        {
            if (go == null)
                return null;

            var attachment = new GameObjectContextAttachment
            {
                Id = Guid.NewGuid().ToString("N"),
                ObjectName = go.name,
                InstanceId = go.GetInstanceID(),
                ComponentNames = go.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToList()
            };

            // Determine if scene object or prefab
            if (go.scene.IsValid())
            {
                // Scene object
                attachment.IsPrefab = false;
                attachment.ScenePath = go.scene.path;
                attachment.HierarchyPath = GetHierarchyPath(go);
            }
            else if (PrefabUtility.IsPartOfPrefabAsset(go))
            {
                // Prefab asset
                attachment.IsPrefab = true;
                attachment.PrefabPath = AssetDatabase.GetAssetPath(go);
                attachment.HierarchyPath = GetHierarchyPath(go);
            }
            else
            {
                // Scene instance of prefab or other
                attachment.IsPrefab = false;
                if (go.scene.IsValid())
                {
                    attachment.ScenePath = go.scene.path;
                }
                attachment.HierarchyPath = GetHierarchyPath(go);
            }

            return attachment;
        }

        /// <summary>
        /// Checks if a file path has a text file extension.
        /// </summary>
        public static bool IsTextFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var extension = Path.GetExtension(path);
            return TextFileExtensions.Contains(extension);
        }

        /// <summary>
        /// Checks if a file is binary by reading the first 8KB for null bytes.
        /// </summary>
        public static bool IsBinaryFile(string fullPath)
        {
            try
            {
                const int bufferSize = 8192;
                var buffer = new byte[bufferSize];

                using (var stream = File.OpenRead(fullPath))
                {
                    var bytesRead = stream.Read(buffer, 0, buffer.Length);
                    return buffer.Take(bytesRead).Any(b => b == 0);
                }
            }
            catch
            {
                return true; // Assume binary if we can't read
            }
        }

        /// <summary>
        /// Reads a file with truncation if it exceeds the size limit.
        /// </summary>
        private static (string Text, bool WasTruncated) ReadFileWithTruncation(string fullPath, long fileSize)
        {
            try
            {
                if (fileSize <= MaxFileSizeBytes)
                {
                    // File is small enough, read all
                    var content = File.ReadAllText(fullPath, Encoding.UTF8);
                    return (content, false);
                }
                else
                {
                    // File is too large, truncate
                    var sb = new StringBuilder();

                    using (var stream = File.OpenRead(fullPath))
                    {
                        // Read head
                        var headBuffer = new byte[TruncationHeadBytes];
                        var headBytesRead = stream.Read(headBuffer, 0, headBuffer.Length);
                        sb.Append(Encoding.UTF8.GetString(headBuffer, 0, headBytesRead));

                        sb.AppendLine();
                        sb.AppendLine($"... [truncated {fileSize - TruncationHeadBytes - TruncationTailBytes} bytes] ...");
                        sb.AppendLine();

                        // Read tail
                        stream.Seek(-TruncationTailBytes, SeekOrigin.End);
                        var tailBuffer = new byte[TruncationTailBytes];
                        var tailBytesRead = stream.Read(tailBuffer, 0, tailBuffer.Length);
                        sb.Append(Encoding.UTF8.GetString(tailBuffer, 0, tailBytesRead));
                    }

                    return (sb.ToString(), true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ContextAttachmentService] Failed to read file {fullPath}: {ex.Message}");
                return ($"<Error reading file: {ex.Message}>", false);
            }
        }

        /// <summary>
        /// Gets the full hierarchy path for a GameObject (e.g., "Canvas/Panel/Button").
        /// </summary>
        private static string GetHierarchyPath(GameObject go)
        {
            if (go == null)
                return "";

            var path = go.name;
            var parent = go.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}
