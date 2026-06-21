// SPDX-License-Identifier: GPL-3.0-only
namespace Ryx.Sidekick.Editor.Presentation.Constants
{
    /// <summary>
    /// Curated pool of inspiring subtitles shown on the empty chat welcome screen.
    /// </summary>
    internal static class WelcomeMessages
    {
        private static readonly string[] Phrases =
        {
            "Let's build something bigger!",
            "What are we shipping today?",
            "Ready when you are.",
            "Let's make something great.",
            "Where should we start?",
            "Let's get to work.",
            "Your next move?",
            "Let's turn ideas into code.",
            "You've come to the absolutely right place!",
            "Big things start small. Let's go.",
        };

        private static int _lastIndex = -1;

        /// <summary>
        /// Returns a random phrase, avoiding an immediate repeat of the previous pick.
        /// </summary>
        public static string Random()
        {
            if (Phrases.Length == 0) return string.Empty;
            if (Phrases.Length == 1) return Phrases[0];

            int index;
            do
            {
                index = UnityEngine.Random.Range(0, Phrases.Length);
            }
            while (index == _lastIndex);

            _lastIndex = index;
            return Phrases[index];
        }
    }
}
