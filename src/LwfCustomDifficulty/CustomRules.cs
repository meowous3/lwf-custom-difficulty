using System;

namespace LwfCustomDifficulty
{
    public enum GrowthMode { Linear, Multiplicative, Exponential }

    public class CustomRules
    {
        /// <summary>Leases add to the repayment target at runtime; this leaves headroom
        /// so the total cannot wrap negative and read as an instant win.</summary>
        public const int MaxValue = int.MaxValue / 4;

        public int FirstRepayment { get; set; } = 10;
        public GrowthMode Mode { get; set; } = GrowthMode.Linear;
        public double GrowthAmount { get; set; } = 20;
        public int Surcharge { get; set; } = 500;
        public int SurchargeEvery { get; set; } = 5;

        public int NextTargetCount(int current, int repaymentIndex)
        {
            double next;
            switch (Mode)
            {
                case GrowthMode.Multiplicative:
                    next = current * GrowthAmount;
                    break;
                // Each repayment adds a step, and every step is the one before it times
                // GrowthAmount, starting from FirstRepayment. The whole curve therefore comes
                // from two numbers the panel already shows: an earlier version accelerated by
                // a constant 1.1 that appeared nowhere in the UI, which left GrowthAmount
                // meaning a different thing here than in the other two modes.
                //
                // The exponent is the index itself, not index-1, so the acceleration applies
                // from the very first step. With index-1 the opening step was FirstRepayment
                // untouched — anything^0 is 1 — so the second demand was always twice the
                // first no matter what GrowthAmount said, and the setting did nothing until
                // the step after that.
                case GrowthMode.Exponential:
                    next = current + FirstRepayment * Math.Pow(GrowthAmount, repaymentIndex);
                    break;
                default:
                    next = current + GrowthAmount;
                    break;
            }

            if (Surcharge > 0 && SurchargeEvery > 0 && repaymentIndex % SurchargeEvery == 0)
            {
                next += Surcharge;
            }

            // Negated rather than `next < current` so that NaN, which compares false against
            // everything, routes to `current` as well. Mono's float->int conversion is not
            // saturating and would otherwise yield int.MinValue here: a permanently negative,
            // permanently satisfied target.
            if (!(next > current)) next = current;   // a multiplier below 1 must not refund
            if (next > MaxValue) next = MaxValue;

            // Ceiling, not truncation: 10 * 1.05 truncates back to 10, and the guard above
            // would then hold the curve flat forever for any multiplier below 1.1.
            return (int)Math.Ceiling(next);
        }
    }
}
