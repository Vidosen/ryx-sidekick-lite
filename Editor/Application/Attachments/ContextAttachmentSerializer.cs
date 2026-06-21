// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using Newtonsoft.Json;
using Ryx.Sidekick.Editor.Domain.Models;

namespace Ryx.Sidekick.Editor.UseCases.Attachments
{
    internal static class ContextAttachmentSerializer
    {
        public static List<SerializedContextAttachment> Serialize(IReadOnlyList<IContextAttachment> attachments)
        {
            if (attachments == null || attachments.Count == 0)
                return new List<SerializedContextAttachment>();

            var result = new List<SerializedContextAttachment>();
            foreach (var attachment in attachments)
            {
                if (attachment == null) continue;

                var serialized = new SerializedContextAttachment();

                switch (attachment)
                {
                    case FileContextAttachment file:
                        serialized.AttachmentType = "File";
                        serialized.JsonData = JsonConvert.SerializeObject(file, ContextAttachmentJson.SerializerSettings);
                        break;
                    case GameObjectContextAttachment go:
                        serialized.AttachmentType = "GameObject";
                        serialized.JsonData = JsonConvert.SerializeObject(go, ContextAttachmentJson.SerializerSettings);
                        break;
                    case ScreenshotContextAttachment screenshot:
                        serialized.AttachmentType = "Screenshot";
                        serialized.JsonData = JsonConvert.SerializeObject(screenshot, ContextAttachmentJson.SerializerSettings);
                        break;
                    default:
                        continue;
                }

                result.Add(serialized);
            }

            return result;
        }

        public static List<IContextAttachment> Deserialize(List<SerializedContextAttachment> serialized)
        {
            if (serialized == null || serialized.Count == 0)
                return new List<IContextAttachment>();

            var result = new List<IContextAttachment>();
            foreach (var item in serialized)
            {
                if (item == null || string.IsNullOrEmpty(item.JsonData))
                    continue;

                try
                {
                    IContextAttachment attachment = item.AttachmentType switch
                    {
                        "File" => JsonConvert.DeserializeObject<FileContextAttachment>(item.JsonData),
                        "GameObject" => JsonConvert.DeserializeObject<GameObjectContextAttachment>(item.JsonData),
                        "Screenshot" => JsonConvert.DeserializeObject<ScreenshotContextAttachment>(item.JsonData),
                        _ => null
                    };

                    if (attachment != null)
                        result.Add(attachment);
                }
                catch
                {
                    // Ignore deserialization errors for individual attachments
                }
            }

            return result;
        }
    }
}
