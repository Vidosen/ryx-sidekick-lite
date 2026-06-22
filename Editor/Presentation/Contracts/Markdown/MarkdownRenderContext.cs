// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Contracts.Markdown
{
    /// <summary>
    /// Shared context passed through the rendering pipeline.
    /// Provides USS class prefixes, element pooling, and extensibility hooks.
    /// </summary>
    internal class MarkdownRenderContext
    {
        public string ClassPrefix { get; set; } = "md-";

        public VisualElement Parent { get; set; }

        public Dictionary<string, VisualTreeAsset> Templates { get; } = new();

        public Dictionary<string, object> UserData { get; } = new();

        /// <summary>
        /// Inline spans (code chips, asset links) emitted while building the rich-text
        /// string for the current block. Populated by inline renderers; consumed by block
        /// renderers to feed <c>MarkdownTextElement</c>. Cleared at the start of each block
        /// render. Offsets are RAW — see <see cref="SpanDescriptor"/>.
        /// </summary>
        public List<SpanDescriptor> Spans { get; } = new();

        public bool UseRichTextForInlines { get; set; } = true;

        public int MaxNestingDepth { get; set; } = 10;

        internal int CurrentDepth { get; set; }

        public Action<string> OnLinkClicked { get; set; }

        public Action<string> OnCodeCopy { get; set; }

        public MarkdownRenderContext() { }

        public MarkdownRenderContext(VisualElement parent)
        {
            Parent = parent;
        }

        public string Class(string name) => $"{ClassPrefix}{name}";
    }
}


