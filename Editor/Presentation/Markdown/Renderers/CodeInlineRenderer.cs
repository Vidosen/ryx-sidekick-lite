// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Text;
using Markdig.Syntax.Inlines;
using Ryx.Sidekick.Editor.Constants;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class CodeInlineRenderer : IMarkdownInlineRenderer
    {
        private const string FontTtfPath = SidekickUiConstants.RobotoMonoFontTtfPath;
        private static Font s_monoFont;
        
        // Marker constants for code inline detection in post-processing
        public const string CodeMarkerStart = "\u2060\u2061\u2062"; // Word joiner + function application + invisible times
        public const string CodeMarkerEnd = "\u2062\u2061\u2060";
        
        public int Priority => 100;

        public bool CanRender(Inline inline) => inline is CodeInline;

        public void Render(Inline inline, StringBuilder builder, VisualElement parent,
            MarkdownRenderContext context, RenderInlineChildrenDelegate renderChildren)
        {
            var code = (CodeInline)inline;
            var content = code.Content;

            if (context.UseRichTextForInlines)
            {
                // Use markers for post-processing in ParagraphBlockRenderer
                if (!context.UserData.ContainsKey("CodeInlines"))
                {
                    context.UserData["CodeInlines"] = new List<string>();
                }
                var codes = (List<string>)context.UserData["CodeInlines"];
                codes.Add(content);
                context.UserData["HasCodeInlines"] = true;
                
                builder?.Append($"{CodeMarkerStart}{content}{CodeMarkerEnd}");
            }
            else
            {
                var codeWrapper = new VisualElement();
                codeWrapper.AddToClassList(context.Class("code-inline"));

                var label = new SelectableLabel(content);
                label.AddToClassList(context.Class("code-inline-text"));
                ApplyMonoFont(label);
                var te = label.Q<UnityEngine.UIElements.TextElement>();
                if (te != null) ApplyMonoFont(te);

                codeWrapper.Add(label);
                parent?.Add(codeWrapper);
            }
        }
        
        public static void ApplyMonoFont(VisualElement element)
        {
            if (element == null)
                return;

            if (s_monoFont == null)
                s_monoFont = AssetDatabase.LoadAssetAtPath<Font>(FontTtfPath);
            
            if (s_monoFont != null)
                element.style.unityFont = s_monoFont;
        }
    }
}


