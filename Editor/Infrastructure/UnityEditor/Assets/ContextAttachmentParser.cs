// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ryx.Sidekick.Editor.Domain.Models;
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure.Assets
{
    internal static class ContextAttachmentParser
    {
        private static readonly Regex ContextBlockRegex = new(
            @"<context_file\b[^>]*>.*?</context_file>|<context_gameobject\b[^>]*>.*?</context_gameobject>|<context_screenshot\b[^>]*/>",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex TruncatedBytesRegex = new(@"\[truncated\s+(?<bytes>\d+)\s+bytes\]", RegexOptions.Compiled);

        public static bool TryExtractContext(string content, out string cleanedText, out List<IContextAttachment> attachments)
        {
            attachments = new List<IContextAttachment>();
            if (string.IsNullOrEmpty(content))
            {
                cleanedText = content;
                return false;
            }

            var matches = ContextBlockRegex.Matches(content);
            if (matches.Count == 0)
            {
                cleanedText = content;
                return false;
            }

            foreach (Match match in matches)
            {
                var attachment = ParseContextTag(match.Value);
                if (attachment != null)
                {
                    attachments.Add(attachment);
                }
            }

            cleanedText = ContextBlockRegex.Replace(content, "");
            cleanedText = cleanedText?.TrimStart('\r', '\n');
            return true;
        }

        private static IContextAttachment ParseContextTag(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;

            try
            {
                var element = XElement.Parse(xml);
                return element.Name.LocalName switch
                {
                    "context_file" => ParseFile(element),
                    "context_gameobject" => ParseGameObject(element),
                    "context_screenshot" => ParseScreenshot(element),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        private static FileContextAttachment ParseFile(XElement element)
        {
            var path = (string)element.Attribute("path");
            if (string.IsNullOrEmpty(path)) return null;

            var content = element.Value ?? "";
            var attachment = new FileContextAttachment
            {
                Id = Guid.NewGuid().ToString("N"),
                FilePath = path,
                Content = content
            };

            var truncatedMatch = TruncatedBytesRegex.Match(content);
            if (truncatedMatch.Success &&
                long.TryParse(truncatedMatch.Groups["bytes"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var truncatedBytes))
            {
                attachment.IsTruncated = true;
                attachment.OriginalSize = truncatedBytes + ContextAttachmentService.TruncationHeadBytes + ContextAttachmentService.TruncationTailBytes;
            }
            else
            {
                attachment.IsTruncated = false;
                attachment.OriginalSize = Encoding.UTF8.GetByteCount(content);
            }

            return attachment;
        }

        private static GameObjectContextAttachment ParseGameObject(XElement element)
        {
            var path = (string)element.Attribute("path");
            if (string.IsNullOrEmpty(path)) return null;

            var scene = (string)element.Attribute("scene");
            var prefab = (string)element.Attribute("prefab");

            var instanceId = 0;
            var instanceIdValue = (string)element.Attribute("instance_id");
            if (!string.IsNullOrEmpty(instanceIdValue))
            {
                int.TryParse(instanceIdValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out instanceId);
            }

            var components = new List<string>();
            var rawComponents = element.Value ?? "";
            if (!string.IsNullOrEmpty(rawComponents))
            {
                var split = rawComponents.Split(',');
                foreach (var item in split)
                {
                    var trimmed = item.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        components.Add(trimmed);
                    }
                }
            }

            return new GameObjectContextAttachment
            {
                Id = Guid.NewGuid().ToString("N"),
                ObjectName = ExtractObjectName(path),
                ScenePath = scene,
                HierarchyPath = path,
                InstanceId = instanceId,
                ComponentNames = components,
                IsPrefab = !string.IsNullOrEmpty(prefab),
                PrefabPath = prefab
            };
        }

        private static ScreenshotContextAttachment ParseScreenshot(XElement element)
        {
            var kindValue = ((string)element.Attribute("kind")) ?? "game";
            var kind = string.Equals(kindValue, "scene", StringComparison.OrdinalIgnoreCase)
                ? ScreenshotKind.SceneView
                : ScreenshotKind.GameView;

            var width = ParseInt(element.Attribute("width"));
            var height = ParseInt(element.Attribute("height"));
            var timestamp = ParseDateTime(element.Attribute("timestamp")) ?? DateTime.Now;

            var attachment = new ScreenshotContextAttachment
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = kind,
                Width = width,
                Height = height,
                Timestamp = timestamp
            };

            if (kind == ScreenshotKind.SceneView)
            {
                var pos = ParseVector3(element.Attribute("camera_pos"));
                var rot = ParseVector3(element.Attribute("camera_rot"));
                var projection = ((string)element.Attribute("projection")) ?? "";
                var isOrtho = string.Equals(projection, "orthographic", StringComparison.OrdinalIgnoreCase);

                attachment.CameraPosition = pos ?? Vector3.zero;
                attachment.CameraRotation = rot.HasValue ? Quaternion.Euler(rot.Value) : Quaternion.identity;
                attachment.IsOrthographic = isOrtho;

                if (isOrtho)
                {
                    attachment.OrthographicSize = ParseFloat(element.Attribute("ortho_size"));
                }
                else
                {
                    attachment.FieldOfView = ParseFloat(element.Attribute("fov"));
                }

                attachment.NearClipPlane = ParseFloat(element.Attribute("near"));
                attachment.FarClipPlane = ParseFloat(element.Attribute("far"));
            }

            return attachment;
        }

        private static string ExtractObjectName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var idx = path.LastIndexOf('/');
            return idx >= 0 ? path[(idx + 1)..] : path;
        }

        private static int ParseInt(XAttribute attr)
        {
            if (attr == null) return 0;
            return int.TryParse(attr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private static float ParseFloat(XAttribute attr)
        {
            if (attr == null) return 0f;
            return float.TryParse(attr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;
        }

        private static Vector3? ParseVector3(XAttribute attr)
        {
            if (attr == null || string.IsNullOrEmpty(attr.Value)) return null;
            var parts = attr.Value.Split(',');
            if (parts.Length != 3) return null;

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return null;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return null;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return null;

            return new Vector3(x, y, z);
        }

        private static DateTime? ParseDateTime(XAttribute attr)
        {
            if (attr == null || string.IsNullOrEmpty(attr.Value)) return null;

            if (DateTime.TryParse(attr.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            {
                return value;
            }

            return null;
        }
    }
}
