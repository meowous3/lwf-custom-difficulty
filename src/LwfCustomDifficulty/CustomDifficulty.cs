using System;
using System.Linq;
using BaseSystem;
using GameState;

namespace LwfCustomDifficulty
{
    /// <summary>
    /// The synthetic Custom difficulty.
    ///
    /// The id is negative so it fails every <c>difficulty &gt; X</c> guard, which is what
    /// keeps the scene-entry throw sites untouched. Being below every defined member it
    /// also sorts first: Custom occupies index 0 of every order array and each vanilla
    /// difficulty sits one place higher than it does in the stock game.
    /// </summary>
    internal static class CustomDifficulty
    {
        internal const string Name = "Custom";

        internal static readonly Difficulty Id = (Difficulty)(-100);

        private static int _firstVanillaIndex = -1;

        internal static bool IsCustom(Difficulty difficulty) => difficulty == Id;

        /// <summary>
        /// The card's description line.
        ///
        /// Read from the configuration rather than fixed, so it cannot contradict the options
        /// panel beside it. The time limit is the one rule the card has no slot of its own
        /// for — the other two slots carry the first repayment and the repayment count — so
        /// it is the only thing worth spending the line on. One line: the slot is one line on
        /// an already full card.
        /// </summary>
        internal static string Describe()
        {
            var minutes = PluginConfig.TimeLimitMinutes;

            return minutes == 0
                ? "Custom rules. No time limit."
                : $"Custom rules. {NumericText.Format(minutes)} minute limit.";
        }

        /// <summary>
        /// Index of the entry directly after Custom — New Customer in a stock build.
        ///
        /// Every index the game derives from a difficulty moves with the array, so the
        /// unlock gating stays correct on its own. The one exception is
        /// <c>GameData.CalculateMaxUnlockedDifficultyIndex</c>, whose "cleared nothing"
        /// branch returns a literal <c>0</c>; that literal does not shift, and would leave
        /// a fresh profile with Custom as its only selectable entry.
        /// </summary>
        internal static int FirstVanillaIndex
        {
            get
            {
                if (_firstVanillaIndex < 0)
                {
                    _firstVanillaIndex = Array.IndexOf(BuildOrder(), Id) + 1;
                }

                return _firstVanillaIndex;
            }
        }

        internal static Difficulty[] BuildOrder()
        {
            return Enum.GetValues(typeof(Difficulty))
                .Cast<Difficulty>()
                .Where(BuildContentPolicy.IsDifficultyIncluded)
                .Where(d => !IsCustom(d))
                .Concat(new[] { Id })
                .OrderBy(d => (int)d)
                .ToArray();
        }
    }
}
