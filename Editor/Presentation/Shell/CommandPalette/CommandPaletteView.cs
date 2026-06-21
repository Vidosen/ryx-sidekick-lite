// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using Ryx.Sidekick.Editor.Presentation.Constants;
using Ryx.Sidekick.Editor.Domain.Commands;
using Ryx.Sidekick.Editor.Presentation.Shell;
using Ryx.Sidekick.Editor.Presentation.UI.Elements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Shell.CommandPalette
{
    /// <summary>
    /// UI Toolkit view for the command palette overlay.
    /// Shows sections, items, and handles visual selection.
    /// </summary>
    internal sealed class CommandPaletteView : VisualElement
    {
        private const string UssClassName = "sk-command-palette";
        private const string UssContainerClassName = UssClassName + "__container";
        private const string UssFilterClassName = UssClassName + "__filter";
        private const string UssListClassName = UssClassName + "__list";
        private const string UssSectionClassName = UssClassName + "__section";
        private const string UssSectionProClassName = UssClassName + "__section--pro";
        private const string UssSectionHeaderClassName = UssClassName + "__section-header";
        private const string UssItemClassName = UssClassName + "__item";
        private const string UssItemSelectedClassName = UssClassName + "__item--selected";
        private const string UssItemLabelClassName = UssClassName + "__item-label";
        private const string UssItemDescClassName = UssClassName + "__item-desc";
        private const string UssItemTrailingClassName = UssClassName + "__item-trailing";
        private const string UssItemLockedClassName = UssClassName + "__item--locked";
        private const string UssEmptyClassName = UssClassName + "__empty";
        private const string UssHiddenClassName = UssClassName + "--hidden";

        private readonly VisualElement _container;
        private readonly TextField _filterField;
        private readonly ScrollView _listScroll;
        private readonly VisualElement _listContainer;
        private readonly Label _emptyLabel;

        private readonly List<CommandAction> _visibleActions = new();
        private readonly Dictionary<string, VisualElement> _itemElements = new();
        private int _selectedIndex = -1;
        private bool _showFilter = true;

        /// <summary>
        /// Fired when user selects an action via Enter/click.
        /// </summary>
        public event Action<CommandAction> OnActionExecute;

        /// <summary>
        /// Fired when user presses Tab on an action (insert).
        /// </summary>
        public event Action<CommandAction> OnActionInsert;

        /// <summary>
        /// Fired when filter text changes.
        /// </summary>
        public event Action<string> OnFilterChanged;

        /// <summary>
        /// Fired when palette should close (Escape).
        /// </summary>
        public event Action OnCloseRequested;

        /// <summary>
        /// Current filter text.
        /// </summary>
        public string FilterText => _filterField?.value ?? "";

        /// <summary>
        /// Whether the palette is visible.
        /// </summary>
        public bool IsVisible => !ClassListContains(UssHiddenClassName);

        public CommandPaletteView()
        {
            AddToClassList(UssClassName);
            AddToClassList(UssHiddenClassName);

            // Main container
            _container = new VisualElement();
            _container.AddToClassList(UssContainerClassName);
            Add(_container);

            // Filter field (shown in general mode, hidden when slash-triggered)
            _filterField = new TextField();
            _filterField.AddToClassList(UssFilterClassName);
            _filterField.RegisterValueChangedCallback(OnFilterValueChanged);
            _container.Add(_filterField);

            // Scrollable list. Horizontal scrolling is forced off: long command descriptions
            // can exceed the viewport on the initial layout pass, and ScrollView would then
            // show the horizontal scroller via inline styles that override the USS hide rule.
            _listScroll = new ScrollView(ScrollViewMode.Vertical);
            _listScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _listScroll.AddToClassList(UssListClassName);
            _container.Add(_listScroll);

            _listContainer = _listScroll.contentContainer;

            // Empty state
            _emptyLabel = new Label("No matching commands");
            _emptyLabel.AddToClassList(UssEmptyClassName);
            _emptyLabel.style.display = DisplayStyle.None;
            _container.Add(_emptyLabel);

            // Register keyboard events on container
            RegisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Shows the palette with the given grouped actions.
        /// </summary>
        public void Show(IReadOnlyList<(string Section, IReadOnlyList<CommandAction> Actions)> groupedActions, bool showFilter = true, string initialFilter = "")
        {
            Debug.Log($"[CommandPalette] View.Show() called - groups: {groupedActions?.Count ?? 0}, showFilter: {showFilter}");
            
            _showFilter = showFilter;
            _filterField.style.display = showFilter ? DisplayStyle.Flex : DisplayStyle.None;
            
            if (!string.IsNullOrEmpty(initialFilter))
            {
                _filterField.SetValueWithoutNotify(initialFilter);
            }
            else
            {
                _filterField.SetValueWithoutNotify("");
            }

            PopulateList(groupedActions);

            RemoveFromClassList(UssHiddenClassName);
            Debug.Log($"[CommandPalette] View visible - hidden class removed, visible actions: {_visibleActions.Count}");
            
            // Focus filter only in general mode; slash-trigger keeps input field focused
            if (showFilter)
            {
                schedule.Execute(() => _filterField.Focus());
            }
            // No focus change needed for slash-trigger mode - input field retains focus
        }

        /// <summary>
        /// Hides the palette.
        /// </summary>
        public void Hide()
        {
            AddToClassList(UssHiddenClassName);
            _filterField.SetValueWithoutNotify("");
        }

        /// <summary>
        /// Updates the list with new grouped actions (e.g., after filter change).
        /// </summary>
        public void UpdateList(IReadOnlyList<(string Section, IReadOnlyList<CommandAction> Actions)> groupedActions)
        {
            PopulateList(groupedActions);
        }

        /// <summary>
        /// Sets the filter text programmatically.
        /// </summary>
        public void SetFilter(string filter)
        {
            _filterField.SetValueWithoutNotify(filter ?? "");
        }

        /// <summary>
        /// Gets the currently selected action, if any.
        /// </summary>
        public CommandAction GetSelectedAction()
        {
            if (_selectedIndex >= 0 && _selectedIndex < _visibleActions.Count)
                return _visibleActions[_selectedIndex];
            return null;
        }

        /// <summary>
        /// Moves selection up by one item (wraps around).
        /// </summary>
        public void SelectPrevious() => MoveSelection(-1);

        /// <summary>
        /// Moves selection down by one item (wraps around).
        /// </summary>
        public void SelectNext() => MoveSelection(1);

        private void PopulateList(IReadOnlyList<(string Section, IReadOnlyList<CommandAction> Actions)> groupedActions)
        {
            _listContainer.Clear();
            _visibleActions.Clear();
            _itemElements.Clear();
            _selectedIndex = -1;
            _listScroll.scrollOffset = Vector2.zero;

            if (groupedActions == null || groupedActions.Count == 0)
            {
                _emptyLabel.style.display = DisplayStyle.Flex;
                _listScroll.style.display = DisplayStyle.None;
                return;
            }

            var hasItems = false;

            foreach (var (section, actions) in groupedActions)
            {
                if (actions == null || actions.Count == 0) continue;

                // Section container
                var sectionElement = new VisualElement();
                sectionElement.AddToClassList(UssSectionClassName);

                // Section header
                var headerLabel = new Label(section?.ToUpperInvariant() ?? string.Empty);
                headerLabel.AddToClassList(UssSectionHeaderClassName);
                sectionElement.Add(headerLabel);

                // Items
                var hasLockedItems = false;
                foreach (var action in actions)
                {
                    var itemElement = CreateItemElement(action);
                    sectionElement.Add(itemElement);
                    _visibleActions.Add(action);
                    _itemElements[action.Id] = itemElement;
                    hasItems = true;
                    hasLockedItems |= action.IsProLocked;
                }

                // Pro-locked content gets a gold "selling" treatment instead of a disabled look.
                if (hasLockedItems)
                {
                    sectionElement.AddToClassList(UssSectionProClassName);
                    headerLabel.text = $"★ {headerLabel.text}";
                }

                _listContainer.Add(sectionElement);
            }

            if (hasItems)
            {
                _emptyLabel.style.display = DisplayStyle.None;
                _listScroll.style.display = DisplayStyle.Flex;
                SelectIndex(0);
            }
            else
            {
                _emptyLabel.style.display = DisplayStyle.Flex;
                _listScroll.style.display = DisplayStyle.None;
            }
        }

        private VisualElement CreateItemElement(CommandAction action)
        {
            var item = new VisualElement();
            item.AddToClassList(UssItemClassName);

            if (action.IsProLocked)
            {
                item.AddToClassList(UssItemLockedClassName);
            }

            var label = new Label(action.Label);
            label.AddToClassList(UssItemLabelClassName);
            item.Add(label);

            if (!string.IsNullOrEmpty(action.Description))
            {
                var desc = new Label(action.Description);
                desc.AddToClassList(UssItemDescClassName);
                item.Add(desc);
            }

            if (!string.IsNullOrEmpty(action.TrailingVisual))
            {
                var trailing = new Label();
                var icon = SidekickIconCatalog.GetIcon(action.TrailingVisual);
                if (icon != null)
                {
                    trailing.AddToClassList(UssItemTrailingClassName + "-icon");
                    SidekickIconCatalog.ApplyToLabel(trailing, action.TrailingVisual, string.Empty, 12f);
                }
                else
                {
                    trailing.text = action.TrailingVisual;
                }

                trailing.AddToClassList(UssItemTrailingClassName);
                item.Add(trailing);
            }

            if (action.IsProLocked)
            {
                // Only sk-pro-badge: the __item-trailing class would override the gold
                // badge background/color with its muted pill styling.
                var proBadge = new Label("PRO");
                proBadge.AddToClassList("sk-pro-badge");
                item.Add(proBadge);
            }

            // Click handler
            item.RegisterCallback<ClickEvent>(_ => ExecuteAction(action));

            // Hover selection
            item.RegisterCallback<MouseEnterEvent>(_ =>
            {
                var index = _visibleActions.IndexOf(action);
                if (index >= 0) SelectIndex(index);
            });

            return item;
        }

        private void SelectIndex(int index)
        {
            // Deselect previous
            if (_selectedIndex >= 0 && _selectedIndex < _visibleActions.Count)
            {
                var prevAction = _visibleActions[_selectedIndex];
                if (_itemElements.TryGetValue(prevAction.Id, out var prevElement))
                {
                    prevElement.RemoveFromClassList(UssItemSelectedClassName);
                }
            }

            _selectedIndex = Mathf.Clamp(index, -1, _visibleActions.Count - 1);

            // Select new
            if (_selectedIndex >= 0 && _selectedIndex < _visibleActions.Count)
            {
                var action = _visibleActions[_selectedIndex];
                if (_itemElements.TryGetValue(action.Id, out var element))
                {
                    element.AddToClassList(UssItemSelectedClassName);
                    ScrollToElement(element);
                }
            }
        }

        private void ScrollToElement(VisualElement element)
        {
            // Ensure element is visible in scroll view
            var elementRect = element.worldBound;
            var scrollRect = _listScroll.worldBound;

            if (elementRect.yMin < scrollRect.yMin)
            {
                _listScroll.scrollOffset = new Vector2(_listScroll.scrollOffset.x, 
                    _listScroll.scrollOffset.y - (scrollRect.yMin - elementRect.yMin));
            }
            else if (elementRect.yMax > scrollRect.yMax)
            {
                _listScroll.scrollOffset = new Vector2(_listScroll.scrollOffset.x,
                    _listScroll.scrollOffset.y + (elementRect.yMax - scrollRect.yMax));
            }
        }

        private void MoveSelection(int delta)
        {
            if (_visibleActions.Count == 0) return;

            var newIndex = _selectedIndex + delta;
            if (newIndex < 0) newIndex = _visibleActions.Count - 1;
            else if (newIndex >= _visibleActions.Count) newIndex = 0;

            SelectIndex(newIndex);
        }

        private void ExecuteAction(CommandAction action)
        {
            if (action == null) return;
            OnActionExecute?.Invoke(action);
        }

        private void InsertAction(CommandAction action)
        {
            if (action == null || !action.SupportsInsert) return;
            OnActionInsert?.Invoke(action);
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (!IsVisible) return;

            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    OnCloseRequested?.Invoke();
                    evt.StopImmediatePropagation();
                    break;

                case KeyCode.UpArrow:
                    MoveSelection(-1);
                    evt.StopImmediatePropagation();
                    break;

                case KeyCode.DownArrow:
                    MoveSelection(1);
                    evt.StopImmediatePropagation();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    var selected = GetSelectedAction();
                    if (selected != null)
                    {
                        ExecuteAction(selected);
                        evt.StopImmediatePropagation();
                    }
                    break;

                case KeyCode.Tab:
                    var selectedForInsert = GetSelectedAction();
                    if (selectedForInsert != null && selectedForInsert.SupportsInsert)
                    {
                        InsertAction(selectedForInsert);
                        evt.StopImmediatePropagation();
                    }
                    break;
            }
        }

        private void OnFilterValueChanged(ChangeEvent<string> evt)
        {
            OnFilterChanged?.Invoke(evt.newValue);
        }
    }
}
