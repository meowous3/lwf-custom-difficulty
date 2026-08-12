using System.Globalization;

namespace LwfCustomDifficulty
{
    /// <summary>
    /// Text to number and back for the options panel's input fields.
    ///
    /// Two things it exists to guarantee. First, one culture throughout:
    /// <c>TMP_InputField</c> validates each typed character against
    /// <c>Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator</c>, so a
    /// panel that displayed and parsed with the invariant culture made a fractional value
    /// un-enterable wherever the separator is not a full stop — the field rejected the only
    /// separator the parser accepted. Everything here therefore defaults to
    /// <see cref="CultureInfo.CurrentCulture"/>, the same one the field validates against.
    ///
    /// Second, out-of-range text clamps rather than falls back. A number too large for
    /// <see cref="int"/> is typeable; returning the previous value there would leave the
    /// screen showing one number and the config holding another, with the normaliser's cap
    /// never applied.
    /// </summary>
    internal static class NumericText
    {
        /// <summary>Fixed-point with trailing zeros trimmed. The general format would emit
        /// exponent notation for very large or very small values, which the field's own
        /// character validation will not accept back.</summary>
        private const string DecimalFormat = "0.##########";

        internal static string Format(int value, CultureInfo culture = null)
        {
            return value.ToString(culture ?? CultureInfo.CurrentCulture);
        }

        internal static string Format(double value, CultureInfo culture = null)
        {
            return value.ToString(DecimalFormat, culture ?? CultureInfo.CurrentCulture);
        }

        /// <summary>Parses whole-number text, clamping anything outside <see cref="int"/> to
        /// the end it ran off. <paramref name="fallback"/> is reached only by text that names
        /// no number at all.</summary>
        internal static int ParseInt(string text, int fallback, CultureInfo culture = null)
        {
            var effective = culture ?? CultureInfo.CurrentCulture;

            // long first, so every value the field's character limit permits is exact.
            if (long.TryParse(text, NumberStyles.Integer, effective, out var whole))
            {
                return Saturate(whole);
            }

            // Longer than a long, or carrying a decimal point. Infinity compares greater than
            // int.MaxValue and saturates; NaN compares false against both bounds, so it is
            // filtered out rather than truncated to int.MinValue.
            if (double.TryParse(text, NumberStyles.Float, effective, out var real) && !double.IsNaN(real))
            {
                return Saturate(real);
            }

            return fallback;
        }

        internal static double ParseDouble(string text, double fallback, CultureInfo culture = null)
        {
            var effective = culture ?? CultureInfo.CurrentCulture;
            return double.TryParse(text, NumberStyles.Float, effective, out var value) ? value : fallback;
        }

        private static int Saturate(long value)
        {
            if (value > int.MaxValue) return int.MaxValue;
            return value < int.MinValue ? int.MinValue : (int)value;
        }

        private static int Saturate(double value)
        {
            if (value > int.MaxValue) return int.MaxValue;
            return value < int.MinValue ? int.MinValue : (int)value;
        }
    }
}
