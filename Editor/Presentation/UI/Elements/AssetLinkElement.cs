// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    /// <summary>
    /// A clickable inline element that displays an asset reference with icon and name.
    /// Single click pings and selects the asset, double click opens it.
    /// </summary>
    [UxmlElement]
    public partial class AssetLinkElement : VisualElement
    {
        [UxmlAttribute("asset-path")]
        public string AssetPathAttribute
        {
            get => AssetPath;
            set => AssetPath = value;
        }

        private readonly Image _icon;
        private readonly Label _nameLabel;
        private string _assetPath;

        public string AssetPath
        {
            get => _assetPath;
            set
            {
                _assetPath = value;
                UpdateDisplay();
            }
        }

        public AssetLinkElement() : this(null) { }

        public AssetLinkElement(string assetPath)
        {
            AddToClassList("md-asset-link");

            _icon = new Image();
            _icon.AddToClassList("md-asset-link__icon");
            Add(_icon);

            _nameLabel = new Label();
            _nameLabel.AddToClassList("md-asset-link__name");
            Add(_nameLabel);

            // Single click: ping and select
            RegisterCallback<ClickEvent>(OnClick);

            // Double click: open asset
            RegisterCallback<MouseDownEvent>(OnMouseDown);

            // Tooltip shows full path
            RegisterCallback<TooltipEvent>(OnTooltip);

            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetPath = assetPath;
            }
        }

        private void UpdateDisplay()
        {
            if (string.IsNullOrEmpty(_assetPath))
            {
                _icon.image = null;
                _icon.style.display = DisplayStyle.None;
                _nameLabel.text = string.Empty;
                tooltip = string.Empty;
                return;
            }

            // Get icon for the asset type
            var icon = AssetLinkService.GetAssetIcon(_assetPath);
            _icon.image = icon;
            
            // Hide icon if not available (removes empty space)
            _icon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;

            // Show file name with extension
            _nameLabel.text = AssetLinkService.GetAssetNameWithExtension(_assetPath);

            // Full path in tooltip
            tooltip = _assetPath;
        }

        private void OnClick(ClickEvent evt)
        {
            if (!string.IsNullOrEmpty(_assetPath))
            {
                AssetLinkService.PingAndSelect(_assetPath);
            }
            evt.StopPropagation();
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.clickCount == 2 && !string.IsNullOrEmpty(_assetPath))
            {
                AssetLinkService.OpenAsset(_assetPath);
                evt.StopPropagation();
            }
        }

        private void OnTooltip(TooltipEvent evt)
        {
            evt.tooltip = _assetPath;
            evt.rect = worldBound;
            evt.StopPropagation();
        }
    }
}

