// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Presentation.Presenters
{
    /// <summary>
    /// Pure step-navigation math for the onboarding wizard. Extracted so the MCP-step skip
    /// (see Documentation~/McpRework/02-onboarding-mcp-step-off.md) is unit-testable without the view.
    /// </summary>
    internal static class OnboardingStepNavigator
    {
        /// <summary>
        /// Next step in <paramref name="direction"/> (+1 forward / -1 back), skipping the MCP step
        /// when <paramref name="includeMcpStep"/> is false. Callers still apply their own
        /// start/last bounds.
        /// </summary>
        public static int NextVisibleStep(int from, int direction, bool includeMcpStep, int mcpStep)
        {
            var next = from + direction;
            if (!includeMcpStep && next == mcpStep)
            {
                next += direction;
            }
            return next;
        }
    }
}
