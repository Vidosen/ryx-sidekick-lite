// SPDX-License-Identifier: GPL-3.0-only
using UnityEditor;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    internal sealed class UnityEditorDialogService : IEditorDialogService
    {
        public bool DisplayDialog(string title, string message, string okButton, string cancelButton = null)
        {
            if (string.IsNullOrEmpty(cancelButton))
            {
                return EditorUtility.DisplayDialog(title, message, okButton);
            }

            return EditorUtility.DisplayDialog(title, message, okButton, cancelButton);
        }
    }
}
