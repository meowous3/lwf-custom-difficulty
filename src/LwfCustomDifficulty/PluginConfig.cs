using System;
using BepInEx.Configuration;

namespace LwfCustomDifficulty
{
    internal static class PluginConfig
    {
        private static ConfigEntry<int> _timeLimit;
        private static ConfigEntry<int> _repayments;
        private static ConfigEntry<int> _firstRepayment;
        private static ConfigEntry<string> _growthMode;
        private static ConfigEntry<double> _growthAmount;
        private static ConfigEntry<int> _surcharge;
        private static ConfigEntry<int> _surchargeEvery;
        private static ConfigEntry<bool> _taxes;

        private static ConfigFile _file;
        private static bool _reloading;

        internal static CustomRules Rules { get; } = new CustomRules();

        internal static int TimeLimitMinutes => ConfigNormalizer.TimeLimitMinutes(_timeLimit.Value);
        internal static int Repayments => ConfigNormalizer.Repayments(_repayments.Value);
        internal static bool TaxesEnabled => _taxes.Value;

        internal static void Bind(ConfigFile file)
        {
            _file = file;
            _timeLimit      = file.Bind("Custom", "TimeLimitMinutes", 30, "0 = none");
            _repayments     = file.Bind("Custom", "Repayments", 5, "");
            _firstRepayment = file.Bind("Custom", "FirstRepayment", 10, "");
            _growthMode     = file.Bind("Custom", "GrowthMode", "Linear", "Linear, Multiplicative, Exponential");
            _growthAmount   = file.Bind("Custom", "GrowthAmount", ConfigNormalizer.DefaultGrowthAmount, "");
            _surcharge      = file.Bind("Custom", "Surcharge", 500, "0 = off");
            _surchargeEvery = file.Bind("Custom", "SurchargeEvery", 5, "");
            _taxes          = file.Bind("Custom", "Taxes", false, "");

            // Rules caches the entry values, so an external reload — ConfigurationManager, or
            // anything else calling ConfigFile.Reload() — would otherwise leave the curve on
            // the old settings while the read-through properties moved to the new ones.
            // Detached first so a second Bind cannot subscribe twice.
            file.ConfigReloaded -= OnConfigReloaded;
            file.ConfigReloaded += OnConfigReloaded;

            Reload();
        }

        private static void OnConfigReloaded(object sender, EventArgs e) => Reload();

        /// <summary>Writes the values through unchecked; Reload is the one place that
        /// normalises, so this path and a hand-edited config file cannot diverge.
        ///
        /// A commit that changes nothing returns without writing. Every input field commits
        /// on losing focus, so tabbing across the options panel would otherwise rewrite the
        /// config file once per row with the values it already held.</summary>
        internal static void Set(int timeLimit, int repayments, int firstRepayment,
                                 GrowthMode mode, double growthAmount, int surcharge, int surchargeEvery,
                                 bool taxes)
        {
            if (timeLimit == _timeLimit.Value
                && repayments == _repayments.Value
                && firstRepayment == _firstRepayment.Value
                && string.Equals(mode.ToString(), _growthMode.Value, StringComparison.Ordinal)
                && growthAmount.Equals(_growthAmount.Value)
                && surcharge == _surcharge.Value
                && surchargeEvery == _surchargeEvery.Value
                && taxes == _taxes.Value)
            {
                return;
            }

            _timeLimit.Value      = timeLimit;
            _repayments.Value     = repayments;
            _firstRepayment.Value = firstRepayment;
            _growthMode.Value     = mode.ToString();
            _growthAmount.Value   = growthAmount;
            _surcharge.Value      = surcharge;
            _surchargeEvery.Value = surchargeEvery;
            _taxes.Value          = taxes;
            Reload();
            _file.Save();
        }

        /// <summary>The single normalisation point. Every value reaching Rules passes through
        /// here, whether it came from Set, from Bind reading the file, or from an external
        /// reload. Out-of-range values are written back to their entries so the file self-heals
        /// instead of disagreeing with the run.</summary>
        private static void Reload()
        {
            // The write-backs below raise SettingChanged, not ConfigReloaded, so this cannot
            // currently re-enter; the flag keeps that true if anything later subscribes more.
            if (_reloading) return;
            _reloading = true;
            try
            {
                Heal(_timeLimit, ConfigNormalizer.TimeLimitMinutes(_timeLimit.Value), "TimeLimitMinutes");
                Heal(_repayments, ConfigNormalizer.Repayments(_repayments.Value), "Repayments");
                Heal(_firstRepayment, ConfigNormalizer.FirstRepayment(_firstRepayment.Value), "FirstRepayment");
                Heal(_surcharge, ConfigNormalizer.Surcharge(_surcharge.Value), "Surcharge");
                Heal(_surchargeEvery, ConfigNormalizer.SurchargeEvery(_surchargeEvery.Value), "SurchargeEvery");
                Heal(_growthAmount, ConfigNormalizer.GrowthAmount(_growthAmount.Value), "GrowthAmount");

                var stored = _growthMode.Value;
                if (!ConfigNormalizer.TryMode(stored, out var mode))
                {
                    Plugin.Log?.LogWarning(
                        $"Config: GrowthMode '{stored}' is not a growth mode; using {mode}.");
                }
                // Assigned directly rather than through Heal: a recognised name differing only
                // in case is canonicalised silently, not reported as a correction.
                if (!string.Equals(stored, mode.ToString(), StringComparison.Ordinal))
                {
                    _growthMode.Value = mode.ToString();
                }

                Rules.FirstRepayment = _firstRepayment.Value;
                Rules.Mode = mode;
                Rules.GrowthAmount = _growthAmount.Value;
                Rules.Surcharge = _surcharge.Value;
                Rules.SurchargeEvery = _surchargeEvery.Value;
            }
            finally
            {
                _reloading = false;
            }
        }

        /// <summary>Writes a corrected value back to its entry and says so in the log. Assigning
        /// only on a real change keeps SettingChanged — and the SaveOnConfigSet write it can
        /// trigger — from firing for values that were already in range.</summary>
        private static void Heal<T>(ConfigEntry<T> entry, T corrected, string key)
        {
            if (Equals(entry.Value, corrected)) return;
            Plugin.Log?.LogWarning($"Config: {key} was {entry.Value}, corrected to {corrected}.");
            entry.Value = corrected;
        }
    }
}
