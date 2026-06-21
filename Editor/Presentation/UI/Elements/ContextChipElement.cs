// SPDX-License-Identifier: GPL-3.0-only
using System;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    /// <summary>
    /// Visual element representing a context attachment chip (file or GameObject).
    /// Displays an icon, name, and optional remove button.
    /// </summary>
    internal class ContextChipElement : VisualElement
    {
        private readonly Image _icon;
        private readonly Label _nameLabel;
        private readonly Label _truncatedIndicator;
        private readonly Button _removeBtn;
        private string _attachmentId;
        private IContextAttachment _attachment;

        public event Action<string> OnRemove;
        public event Action<IContextAttachment> OnClick;

        public ContextChipElement()
        {
            AddToClassList("sk-context-chip");

            // Icon
            _icon = new Image();
            _icon.AddToClassList("sk-context-chip__icon");
            Add(_icon);

            // Name label
            _nameLabel = new Label();
            _nameLabel.AddToClassList("sk-context-chip__name");
            Add(_nameLabel);

            // Truncated indicator (shown for truncated files only)
            _truncatedIndicator = new Label("…");
            _truncatedIndicator.AddToClassList("sk-context-chip__truncated");
            _truncatedIndicator.style.display = DisplayStyle.None;
            Add(_truncatedIndicator);

            // Remove button
            _removeBtn = new Button { text = "×" };
            _removeBtn.AddToClassList("sk-context-chip__remove");
            _removeBtn.clicked += () => OnRemove?.Invoke(_attachmentId);
            Add(_removeBtn);

            // Click handler for the whole chip (except remove button)
            RegisterCallback<ClickEvent>(HandleClick);
        }

        private void HandleClick(ClickEvent evt)
        {
            // Don't trigger click if user clicked the remove button
            if (evt.target == _removeBtn || _removeBtn.Contains((VisualElement)evt.target))
                return;

            OnClick?.Invoke(_attachment);
            evt.StopPropagation();
        }

        /// <summary>
        /// Sets the attachment data and updates the visual representation.
        /// </summary>
        public void SetAttachment(IContextAttachment attachment, bool showRemoveButton = true)
        {
            _attachment = attachment;
            _attachmentId = attachment.Id;
            _nameLabel.text = attachment.DisplayName;
            _truncatedIndicator.style.display = DisplayStyle.None;

            // Set remove button visibility
            _removeBtn.style.display = showRemoveButton ? DisplayStyle.Flex : DisplayStyle.None;

            // Set icon and tooltip based on type
            switch (attachment)
            {
                case FileContextAttachment file:
                    SetupFileChip(file);
                    break;

                case GameObjectContextAttachment go:
                    SetupGameObjectChip(go);
                    break;

                case ScreenshotContextAttachment screenshot:
                    SetupScreenshotChip(screenshot);
                    break;

                default:
                    tooltip = attachment.DisplayName;
                    break;
            }
        }

        private void SetupFileChip(FileContextAttachment file)
        {
            // Get icon from asset database
            var icon = AssetLinkService.GetAssetIcon(file.FilePath);
            _icon.image = icon ? icon :
                // Fallback to document icon
                EditorGUIUtility.IconContent("TextAsset Icon").image;

            // Add file type class for styling
            AddToClassList("sk-context-chip--file");

            // Show truncated indicator when content was truncated.
            if (file.IsTruncated)
            {
                _truncatedIndicator.style.display = DisplayStyle.Flex;
            }

            // Tooltip with full path and size info
            var sizeInfo = file.IsTruncated
                ? $" (truncated, original: {FormatFileSize(file.OriginalSize)})"
                : $" ({FormatFileSize(file.OriginalSize)})";
            tooltip = $"{file.FilePath}{sizeInfo}";
        }

        private void SetupGameObjectChip(GameObjectContextAttachment go)
        {
            // GameObject icon
            _icon.image = EditorGUIUtility.IconContent("GameObject Icon").image;

            // Add GameObject type class for styling
            AddToClassList("sk-context-chip--gameobject");

            // Tooltip with full context info
            if (go.IsPrefab && !string.IsNullOrEmpty(go.PrefabPath))
            {
                tooltip = $"Prefab: {go.PrefabPath}\nPath: {go.HierarchyPath}\nComponents: {string.Join(", ", go.ComponentNames ?? new System.Collections.Generic.List<string>())}";
            }
            else if (!string.IsNullOrEmpty(go.ScenePath))
            {
                tooltip = $"Scene: {go.ScenePath}\nPath: {go.HierarchyPath}\nComponents: {string.Join(", ", go.ComponentNames ?? new System.Collections.Generic.List<string>())}";
            }
            else
            {
                tooltip = $"Path: {go.HierarchyPath}\nComponents: {string.Join(", ", go.ComponentNames ?? new System.Collections.Generic.List<string>())}";
            }
        }

        private void SetupScreenshotChip(ScreenshotContextAttachment screenshot)
        {
            // Use Unity's internal editor window icons (d_UnityEditor.* pattern)
            var iconName = screenshot.Kind == ScreenshotKind.SceneView
                ? "d_UnityEditor.SceneView"
                : "d_UnityEditor.GameView";

            var iconTexture = TryGetIcon(iconName);

            // Fallback to generic icons if window icons not found
            iconTexture ??= TryGetIcon("Texture Icon") ?? TryGetIcon("RawImage Icon");

            _icon.image = iconTexture;

            // Add screenshot type class for styling
            AddToClassList("sk-context-chip--screenshot");
            AddToClassList(screenshot.Kind == ScreenshotKind.SceneView
                ? "sk-context-chip--scene-view"
                : "sk-context-chip--game-view");

            // Build tooltip with metadata
            var tooltipLines = new System.Collections.Generic.List<string>
            {
                $"{screenshot.DisplayName} Screenshot",
                $"Size: {screenshot.Width}×{screenshot.Height}",
                $"Captured: {screenshot.Timestamp:HH:mm:ss}"
            };

            if (screenshot.Kind == ScreenshotKind.SceneView)
            {
                var pos = screenshot.CameraPosition;
                var rot = ((Quaternion)screenshot.CameraRotation).eulerAngles;
                tooltipLines.Add($"Camera: ({pos.x:F1}, {pos.y:F1}, {pos.z:F1})");
                tooltipLines.Add($"Rotation: ({rot.x:F0}°, {rot.y:F0}°, {rot.z:F0}°)");

                if (screenshot.IsOrthographic)
                    tooltipLines.Add($"Orthographic (size: {screenshot.OrthographicSize:F1})");
                else
                    tooltipLines.Add($"Perspective (FOV: {screenshot.FieldOfView:F0}°)");
            }

            tooltip = string.Join("\n", tooltipLines);
        }

        /// <summary>
        /// Tries to get an icon by name, returning null if not found.
        /// </summary>
        private static Texture TryGetIcon(string iconName)
        {
            try
            {
                var content = EditorGUIUtility.IconContent(iconName);
                return content?.image;
            }
            catch
            {
                return null;
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024} KB";
            return $"{bytes / (1024 * 1024)} MB";
        }

        /// <summary>
        /// Handles click to ping/select the asset or GameObject in Unity.
        /// </summary>
        public static void HandleContextChipClick(IContextAttachment attachment)
        {
            if (attachment == null)
                return;

            switch (attachment)
            {
                case FileContextAttachment file:
                    // Ping and select the asset
                    AssetLinkService.PingAndSelect(file.FilePath);
                    break;

                case GameObjectContextAttachment go:
                    // Try to find and select the GameObject
                    TrySelectGameObject(go);
                    break;
            }
        }

        private static void TrySelectGameObject(GameObjectContextAttachment go)
        {
            GameObject foundObject = null;

            // Try to find by InstanceID first (for current scene objects)
            var obj = EditorUtility.EntityIdToObject(go.InstanceId);
            if (obj is GameObject gameObj)
            {
                foundObject = gameObj;
            }
            else if (go.IsPrefab && !string.IsNullOrEmpty(go.PrefabPath))
            {
                // Load prefab asset
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(go.PrefabPath);
                if (prefab)
                {
                    foundObject = prefab;
                }
            }
            else if (!string.IsNullOrEmpty(go.ScenePath))
            {
                // Try to find in loaded scenes by hierarchy path
                var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var sceneObj in allObjects)
                {
                    if (GetHierarchyPath(sceneObj) == go.HierarchyPath && sceneObj.scene.path == go.ScenePath)
                    {
                        foundObject = sceneObj;
                        break;
                    }
                }
            }

            // Select if found
            if (foundObject)
            {
                Selection.activeGameObject = foundObject;
                EditorGUIUtility.PingObject(foundObject);
            }
            else
            {
                Debug.LogWarning($"[ContextChipElement] Could not find GameObject: {go.HierarchyPath}");
            }
        }

        private static string GetHierarchyPath(GameObject go)
        {
            if (go == null)
                return "";

            var path = go.name;
            var parent = go.transform.parent;

            while (parent)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}
