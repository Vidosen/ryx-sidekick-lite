// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace Ryx.Sidekick.Editor
{
    internal enum EditorSelectionKind
    {
        None,
        GameObject,
        File,
        Asset
    }

    internal readonly struct EditorSelectionInfo
    {
        public bool HasSelection { get; }
        public string DisplayName { get; }
        public EditorSelectionKind Kind { get; }

        public EditorSelectionInfo(bool hasSelection, string displayName, EditorSelectionKind kind)
        {
            HasSelection = hasSelection;
            DisplayName = displayName ?? string.Empty;
            Kind = kind;
        }

        public static EditorSelectionInfo Empty { get; } =
            new EditorSelectionInfo(false, string.Empty, EditorSelectionKind.None);
    }

    /// <summary>
    /// Abstraction over the active editor selection so that Presentation-layer
    /// view-models can react to selection changes without depending on
    /// editor-specific APIs.
    /// </summary>
    internal interface IEditorSelectionService
    {
        event Action SelectionChanged;
        EditorSelectionInfo Current { get; }
    }
}
