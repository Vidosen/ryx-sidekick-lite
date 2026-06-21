// SPDX-License-Identifier: GPL-3.0-only
using System;
using UnityEditor;

namespace Ryx.Sidekick.Editor.Infrastructure
{
    internal sealed class UnityEditorScheduler : IEditorScheduler
    {
        public void Schedule(Action action)
        {
            if (action == null)
            {
                return;
            }

            EditorApplication.delayCall += () => action();
        }
    }
}
