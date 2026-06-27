// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Renderers
{
    /// <summary>
    /// Renders a JSON token as syntax-coloured, per-line VisualElement rows (keys / strings /
    /// numbers / keywords / punctuation), mirroring the design's MCP I/O blocks. Uses one Label
    /// per coloured segment (no rich text) so values containing <c>&lt;</c>, <c>{</c>, etc. never
    /// break. Falls back to a plain monospace block on parse failure or when the payload is large.
    /// </summary>
    internal static class JsonHighlighter
    {
        private const int MaxChars = 12000; // serialized length above which we skip per-segment colouring
        private const float IndentPx = 14f;

        private readonly struct Seg
        {
            public readonly string Text;
            public readonly string Modifier;

            public Seg(string text, string modifier)
            {
                Text = text;
                Modifier = modifier;
            }
        }

        /// <summary>Coloured view of an object/array token; plain block for bare strings / large payloads.</summary>
        internal static VisualElement BuildColoredJson(JToken token)
        {
            if (token == null)
            {
                return PlainBlock("");
            }

            if (token.Type == JTokenType.String)
            {
                return PlainBlock(token.ToString());
            }

            string serialized;
            try
            {
                serialized = token.ToString(Formatting.Indented);
            }
            catch
            {
                serialized = token.ToString();
            }

            if (serialized.Length > MaxChars)
            {
                return PlainBlock(serialized);
            }

            try
            {
                var container = new VisualElement();
                container.AddToClassList("sk-mcp-json");
                Emit(container, 0, null, token, false);
                return container;
            }
            catch
            {
                return PlainBlock(serialized);
            }
        }

        /// <summary>Try to parse <paramref name="raw"/> as JSON; colourise objects/arrays, else plain text.</summary>
        internal static VisualElement BuildFromMaybeJson(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return PlainBlock("");
            }

            JToken token = null;
            try
            {
                token = JToken.Parse(raw);
            }
            catch
            {
                // not JSON — fall through to plain text
            }

            if (token != null && (token.Type == JTokenType.Object || token.Type == JTokenType.Array))
            {
                return BuildColoredJson(token);
            }

            return PlainBlock(raw);
        }

        /// <summary>Header meta text, e.g. <c>{ 3 keys }</c> / <c>[ 2 items ]</c>; null when not applicable.</summary>
        internal static string DescribeInput(JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    return obj.Count == 1 ? "{ 1 key }" : $"{{ {obj.Count} keys }}";
                case JArray arr:
                    return arr.Count == 1 ? "[ 1 item ]" : $"[ {arr.Count} items ]";
                default:
                    return null;
            }
        }

        private static void Emit(VisualElement parent, int depth, string propName, JToken token, bool trailingComma)
        {
            var keySegs = new List<Seg>();
            if (propName != null)
            {
                keySegs.Add(new Seg(JsonConvert.ToString(propName), "key"));
                keySegs.Add(new Seg(": ", "punct"));
            }

            switch (token.Type)
            {
                case JTokenType.Object:
                    var obj = (JObject)token;
                    if (obj.Count == 0)
                    {
                        AddLine(parent, depth, Append(keySegs, new Seg(trailingComma ? "{}," : "{}", "punct")));
                        break;
                    }

                    AddLine(parent, depth, Append(keySegs, new Seg("{", "punct")));
                    var props = obj.Properties().ToList();
                    for (var i = 0; i < props.Count; i++)
                    {
                        Emit(parent, depth + 1, props[i].Name, props[i].Value, i < props.Count - 1);
                    }
                    AddLine(parent, depth, new[] { new Seg(trailingComma ? "}," : "}", "punct") });
                    break;

                case JTokenType.Array:
                    var arr = (JArray)token;
                    if (arr.Count == 0)
                    {
                        AddLine(parent, depth, Append(keySegs, new Seg(trailingComma ? "[]," : "[]", "punct")));
                        break;
                    }

                    AddLine(parent, depth, Append(keySegs, new Seg("[", "punct")));
                    for (var i = 0; i < arr.Count; i++)
                    {
                        Emit(parent, depth + 1, null, arr[i], i < arr.Count - 1);
                    }
                    AddLine(parent, depth, new[] { new Seg(trailingComma ? "]," : "]", "punct") });
                    break;

                default:
                    var value = FormatPrimitive(token);
                    var segs = new List<Seg>(keySegs) { value };
                    if (trailingComma)
                    {
                        segs.Add(new Seg(",", "punct"));
                    }
                    AddLine(parent, depth, segs.ToArray());
                    break;
            }
        }

        private static Seg[] Append(List<Seg> head, Seg tail)
        {
            var list = new List<Seg>(head) { tail };
            return list.ToArray();
        }

        private static Seg FormatPrimitive(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.String:
                case JTokenType.Date:
                case JTokenType.Guid:
                case JTokenType.Uri:
                case JTokenType.TimeSpan:
                    return new Seg(JsonConvert.ToString(token.ToString()), "string");
                case JTokenType.Integer:
                case JTokenType.Float:
                    return new Seg(token.ToString(), "number");
                case JTokenType.Boolean:
                    return new Seg(token.Value<bool>() ? "true" : "false", "keyword");
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return new Seg("null", "keyword");
                default:
                    return new Seg(token.ToString(), "string");
            }
        }

        private static void AddLine(VisualElement parent, int depth, Seg[] segments)
        {
            var row = new VisualElement();
            row.AddToClassList("sk-mcp-json-line");
            row.style.paddingLeft = depth * IndentPx;

            foreach (var seg in segments)
            {
                var label = new Label(seg.Text);
                label.AddToClassList("sk-mcp-json-seg");
                label.AddToClassList($"sk-mcp-json-seg--{seg.Modifier}");
                label.selection.isSelectable = true;
                row.Add(label);
            }

            parent.Add(row);
        }

        private static VisualElement PlainBlock(string text)
        {
            var label = new Label(text ?? "");
            label.AddToClassList("sk-mcp-output-text");
            label.selection.isSelectable = true;
            return label;
        }
    }
}
