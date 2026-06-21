// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    /// <summary>
    /// A centered banner element for system notifications like domain reload.
    /// Renders as a distinct visual element separate from User/Assistant message bubbles.
    /// </summary>
    [UxmlElement]
    public partial class SystemBannerElement : VisualElement
    {
        public enum BannerType
        {
            DomainReload
        }

        private readonly Image _icon;
        private readonly Label _textLabel;

        public SystemBannerElement() : this(BannerType.DomainReload, "System notification") { }

        public SystemBannerElement(BannerType type, string message)
        {
            AddToClassList("sk-system-banner");

            var content = new VisualElement();
            content.AddToClassList("sk-system-banner__content");

            _icon = new Image();
            _icon.AddToClassList("sk-system-banner__icon");
            content.Add(_icon);

            _textLabel = new Label(message);
            _textLabel.AddToClassList("sk-system-banner__text");
            content.Add(_textLabel);

            Add(content);

            SetBannerType(type);
        }

        private void SetBannerType(BannerType type)
        {
            switch (type)
            {
                case BannerType.DomainReload:
                    var refreshIcon = EditorGUIUtility.IconContent("d_Refresh");
                    _icon.image = refreshIcon?.image;
                    break;
            }
        }

        public void SetMessage(string message)
        {
            _textLabel.text = message;
        }
    }
}
