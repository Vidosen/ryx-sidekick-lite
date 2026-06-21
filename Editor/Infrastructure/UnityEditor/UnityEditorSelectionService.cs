// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEditor;
using UnityEngine;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    /// <summary>
    /// Infrastructure adapter wrapping UnityEditor.Selection so that view-models
    /// can observe selection changes without depending on UnityEditor.
    /// </summary>
    internal sealed class UnityEditorSelectionService : IEditorSelectionService, IDisposable
    {
        public event Action SelectionChanged;

        public UnityEditorSelectionService()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        public EditorSelectionInfo Current
        {
            get
            {
                var obj = Selection.activeObject;
                if (obj == null)
                {
                    return EditorSelectionInfo.Empty;
                }

                if (obj is GameObject)
                {
                    return new EditorSelectionInfo(true, obj.name, EditorSelectionKind.GameObject);
                }

                var path = AssetDatabase.GetAssetPath(obj);
                var kind = !string.IsNullOrEmpty(path) ? EditorSelectionKind.File : EditorSelectionKind.Asset;
                return new EditorSelectionInfo(true, obj.name, kind);
            }
        }

        public void Dispose()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnSelectionChanged()
        {
            SelectionChanged?.Invoke();
        }
    }
}
