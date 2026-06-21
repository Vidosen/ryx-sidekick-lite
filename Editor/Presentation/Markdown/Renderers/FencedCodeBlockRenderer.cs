// SPDX-License-Identifier: GPL-3.0-only
using Markdig.Syntax;
using UnityEngine.UIElements;

using Ryx.Sidekick.Editor.Presentation.Contracts.Markdown;

namespace Ryx.Sidekick.Editor.Infrastructure.Markdown.Renderers
{
    internal class FencedCodeBlockRenderer : IMarkdownBlockRenderer
    {
        public int Priority => 90;

        public bool CanRender(Block block) => block is FencedCodeBlock;

        public void Render(Block block, VisualElement parent, MarkdownRenderContext context, RenderChildrenDelegate renderChildren)
        {
            var codeBlock = (FencedCodeBlock)block;
            var code = codeBlock.Lines.ToString();
            var language = codeBlock.Info ?? "";
            var displayLanguage = string.IsNullOrEmpty(language) ? string.Empty : language.ToUpperInvariant();

            if (context.Templates.TryGetValue("FencedCodeBlock", out var template) && template != null)
            {
                var instance = template.Instantiate();
                instance.AddToClassList(context.Class("code-block"));

                var codeLabel = instance.Q<Label>("code-content") ?? instance.Q<Label>();
                if (codeLabel != null)
                {
                    codeLabel.text = code;
                }

                var langLabel = instance.Q<Label>("code-language");
                if (langLabel != null)
                {
                    langLabel.text = displayLanguage;
                    langLabel.style.display = string.IsNullOrEmpty(displayLanguage) ? DisplayStyle.None : DisplayStyle.Flex;
                }

                parent.Add(instance);
                return;
            }

            var container = new VisualElement();
            container.AddToClassList(context.Class("code-block"));
            if (!string.IsNullOrEmpty(language))
            {
                container.AddToClassList(context.Class($"lang-{language.ToLowerInvariant()}"));
            }

            var header = new VisualElement();
            header.AddToClassList(context.Class("code-header"));

            if (!string.IsNullOrEmpty(displayLanguage))
            {
                var langLabel = new Label(displayLanguage);
                langLabel.AddToClassList(context.Class("code-lang"));
                header.Add(langLabel);
            }

            var copyBtn = new Button(() => context.OnCodeCopy?.Invoke(code))
            {
                text = "Copy"
            };
            copyBtn.AddToClassList(context.Class("code-copy"));
            header.Add(copyBtn);

            container.Add(header);

            var codeContent = new Label(code);
            codeContent.AddToClassList(context.Class("code-content"));
            codeContent.selection.isSelectable = true;
            container.Add(codeContent);

            parent.Add(container);
        }
    }
}


