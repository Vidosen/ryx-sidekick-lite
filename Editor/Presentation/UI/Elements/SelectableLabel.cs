// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    /// <summary>
    /// A label-like element that supports text selection and copying.
    /// Based on TextField with readOnly=true for native selection support.
    /// </summary>
    [UxmlElement("selectable-label")]
    public partial class SelectableLabel : TextField
    {
        public SelectableLabel() : this(string.Empty)
        {
        }

        public SelectableLabel(string text)
        {
            SetValueWithoutNotify(text);
            isReadOnly = true;
            multiline = true;

            // Disable vertical scrollbar (TextField doesn't have horizontal scroller)
            verticalScrollerVisibility = ScrollerVisibility.Hidden;
            
            // Prevent shrinking
            style.flexShrink = 0;

            // Make it look like a label by hiding the TextField styling
            AddToClassList("sk-selectable-label");

            // Remove default TextField classes that add borders/backgrounds
            RemoveFromClassList("unity-text-field");

            // Make the text element more accessible
            var textInput = this.Q<TextElement>();
            if (textInput != null)
            {
                textInput.enableRichText = false;
                textInput.selection.isSelectable = true;
            }
            
            // Fix internal elements after attached to panel
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }
        
        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // Disable scrolling and clipping on ALL internal elements
            this.Query<VisualElement>().ForEach(element =>
            {
                // Prevent shrinking on all elements
                element.style.flexShrink = 0;
                
                // If it's a ScrollView, disable its clipping behavior
                if (element is ScrollView sv)
                {
                    sv.mode = ScrollViewMode.Vertical;
                    sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                    sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                    // Try to disable viewport clipping
                    var viewport = sv.Q(className: "unity-scroll-view__content-viewport");
                    if (viewport != null)
                    {
                        viewport.style.overflow = Overflow.Visible;
                    }
                }
            });
            
            // Set white-space on text input
            var textInputElement = this.Q(className: "unity-text-input");
            if (textInputElement != null)
            {
                textInputElement.style.whiteSpace = WhiteSpace.Normal;
            }
            
            // Ensure TextElement wraps
            var textElement = this.Q<TextElement>();
            if (textElement != null)
            {
                textElement.style.whiteSpace = WhiteSpace.Normal;
            }
        }

        public sealed override void SetValueWithoutNotify(string newValue)
        {
            base.SetValueWithoutNotify(newValue);
        }

        /// <summary>
        /// Initial text when instantiated from UXML.
        /// </summary>
        [UxmlAttribute("text")]
        public string UxmlText
        {
            get => value;
            set => SetValueWithoutNotify(value ?? string.Empty);
        }

        /// <summary>
        /// Enable or disable rich text rendering.
        /// </summary>
        public bool enableRichText
        {
            get
            {
                var textElement = this.Q<TextElement>();
                return textElement?.enableRichText ?? false;
            }
            set
            {
                var textElement = this.Q<TextElement>();
                if (textElement != null)
                {
                    textElement.enableRichText = value;
                }
            }
        }
    }
}
