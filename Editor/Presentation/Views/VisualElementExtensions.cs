// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    /// <summary>
    /// Small UI Toolkit helpers shared by code-built views (e.g. modal cards).
    /// </summary>
    internal static class VisualElementExtensions
    {
        /// <summary>Adds a USS class and returns the element, for fluent construction.</summary>
        internal static T WithClass<T>(this T element, string className) where T : VisualElement
        {
            element.AddToClassList(className);
            return element;
        }
    }
}
