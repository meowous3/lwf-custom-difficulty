using System;

namespace LwfCustomDifficulty
{
    /// <summary>Pure normalisation of the eight stored settings: no BepInEx dependency, so it
    /// links into the test project the same way CustomRules does. Every value reaching
    /// <see cref="CustomRules"/> passes through here, whether the player set it in the UI or
    /// hand-edited it into the config file.</summary>
    internal static class ConfigNormalizer
    {
        internal const double DefaultGrowthAmount = 20d;

        internal static int Clamp(int value, int min = 0)
        {
            if (value < min) return min;
            return value > CustomRules.MaxValue ? CustomRules.MaxValue : value;
        }

        internal static int TimeLimitMinutes(int value) => Clamp(value);

        /// <summary>Zero repayments would be a run that is won the moment it starts.</summary>
        internal static int Repayments(int value) => Clamp(value, 1);

        /// <summary>A target of zero or less is permanently satisfied.</summary>
        internal static int FirstRepayment(int value) => Clamp(value, 1);

        internal static int Surcharge(int value) => Clamp(value);

        /// <summary>Only ever used as a modulus divisor, so it needs a floor but no cap.</summary>
        internal static int SurchargeEvery(int value) => value < 1 ? 1 : value;

        /// <summary>NaN and the infinities survive a round trip through the config file and are
        /// parseable out of a text field. ECMA-335 leaves double-&gt;int conversion of them
        /// unspecified: Mono on x64 yields int.MinValue rather than saturating, which would be a
        /// permanently satisfied negative target. Substitute the default rather than propagate.</summary>
        internal static double GrowthAmount(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return DefaultGrowthAmount;
            return value < 0 ? 0 : value;
        }

        /// <summary>Parses a stored mode name. Returns false when the text did not name a mode
        /// and <paramref name="mode"/> fell back to Linear, so the caller can report it; a
        /// recognised name that merely differs in case parses successfully.</summary>
        internal static bool TryMode(string value, out GrowthMode mode)
        {
            // ignoreCase: the strict overload would turn a hand-edited "exponential" into Linear,
            // and the write-back would then erase the spelling that revealed the mistake.
            // IsDefined as well as TryParse: TryParse accepts any numeric string, so "7" would
            // otherwise become an undefined GrowthMode.
            if (!string.IsNullOrEmpty(value)
                && Enum.TryParse(value, true, out GrowthMode parsed)
                && Enum.IsDefined(typeof(GrowthMode), parsed))
            {
                mode = parsed;
                return true;
            }

            mode = GrowthMode.Linear;
            return false;
        }
    }
}
