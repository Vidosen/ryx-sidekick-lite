// SPDX-License-Identifier: GPL-3.0-only
using Ryx.Sidekick.Editor.Infrastructure.Assets;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.UI.Elements
{
    /// <summary>
    /// A clickable element for Write/Edit tool results.
    /// Shows file badge (icon + name) styled like AssetLinkElement,
    /// with optional diff summary on second line ("Added X lines").
    /// Click opens the file in the external IDE at the specified line.
    /// </summary>
    [UxmlElement]
    public partial class EditLinkElement : VisualElement
    {
        private readonly VisualElement _badge;
        private readonly Image _icon;
        private readonly Label _nameLabel;
        private readonly Label _summaryLabel;
        private string _assetPath;
        private int _lineNumber;

        public string AssetPath
        {
            get => _assetPath;
            set
            {
                _assetPath = value;
                UpdateDisplay();
            }
        }

        public int LineNumber
        {
            get => _lineNumber;
            set => _lineNumber = value;
        }

        public EditLinkElement() : this(null, 1, null) { }

        public EditLinkElement(string assetPath, int lineNumber, string diffSummary = null)
        {
            AddToClassList("sk-edit-link");

            // First row: clickable badge (same style as AssetLinkElement)
            _badge = new VisualElement();
            _badge.AddToClassList("sk-edit-link__badge");

            _icon = new Image();
            _icon.AddToClassList("sk-edit-link__icon");
            _badge.Add(_icon);

            _nameLabel = new Label();
            _nameLabel.AddToClassList("sk-edit-link__name");
            _badge.Add(_nameLabel);

            Add(_badge);

            // Second row: summary label
            _summaryLabel = new Label();
            _summaryLabel.AddToClassList("sk-edit-link__summary");
            Add(_summaryLabel);

            _lineNumber = lineNumber;

            // Single click on badge: open at line
            _badge.RegisterCallback<ClickEvent>(OnClick);

            // Tooltip shows full path and line
            _badge.RegisterCallback<TooltipEvent>(OnTooltip);

            if (!string.IsNullOrEmpty(assetPath))
            {
                _assetPath = assetPath;
                UpdateDisplay();
            }

            SetDiffSummary(diffSummary);
        }

        public void SetDiffSummary(string summary)
        {
            if (string.IsNullOrEmpty(summary))
            {
                _summaryLabel.style.display = DisplayStyle.None;
                return;
            }

            _summaryLabel.text = summary;
            _summaryLabel.style.display = DisplayStyle.Flex;
        }

        private void UpdateDisplay()
        {
            if (string.IsNullOrEmpty(_assetPath))
            {
                _icon.image = null;
                _icon.style.display = DisplayStyle.None;
                _nameLabel.text = string.Empty;
                _badge.tooltip = string.Empty;
                return;
            }

            // Get icon for the asset type
            var icon = AssetLinkService.GetAssetIcon(_assetPath);
            _icon.image = icon;

            // Hide icon if not available
            _icon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;

            // Show file name with extension
            _nameLabel.text = AssetLinkService.GetAssetNameWithExtension(_assetPath);

            // Full path in tooltip
            _badge.tooltip = _lineNumber > 1 ? $"{_assetPath}:{_lineNumber}" : _assetPath;
        }

        private void OnClick(ClickEvent evt)
        {
            if (!string.IsNullOrEmpty(_assetPath))
            {
                AssetLinkService.OpenAssetAtLine(_assetPath, _lineNumber);
            }
            evt.StopPropagation();
        }

        private void OnTooltip(TooltipEvent evt)
        {
            evt.tooltip = _lineNumber > 1 ? $"{_assetPath}:{_lineNumber}" : _assetPath;
            evt.rect = _badge.worldBound;
            evt.StopPropagation();
        }
    }
}
