// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine.UIElements;

namespace Ryx.Sidekick.Editor.Presentation.Views
{
    internal enum IndicatorState
    {
        Neutral,
        Success,
        Warning,
        Error,
        Checking
    }

    internal static class ViewIndicatorStyler
    {
        public static void Apply(VisualElement indicator, IndicatorState state)
        {
            if (indicator == null)
            {
                return;
            }

            indicator.RemoveFromClassList("success");
            indicator.RemoveFromClassList("error");
            indicator.RemoveFromClassList("warning");
            indicator.RemoveFromClassList("checking");

            switch (state)
            {
                case IndicatorState.Success:
                    indicator.AddToClassList("success");
                    break;
                case IndicatorState.Warning:
                    indicator.AddToClassList("warning");
                    break;
                case IndicatorState.Error:
                    indicator.AddToClassList("error");
                    break;
                case IndicatorState.Checking:
                    indicator.AddToClassList("checking");
                    break;
            }
        }
    }
}
