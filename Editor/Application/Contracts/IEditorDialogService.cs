// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor
{
    internal interface IEditorDialogService
    {
        bool DisplayDialog(string title, string message, string okButton, string cancelButton = null);
    }
}
