using LwfCustomDifficulty;
using Xunit;

public class ConfigNormalizerTests
{
    // A value out of range in the config file is not hypothetical: the file is plain text
    // next to the game and nothing stops a player editing it. Every guard below exists
    // because the un-normalised value would reach CustomRules and misbehave.

    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(int.MaxValue, CustomRules.MaxValue)]
    public void FirstRepayment_is_at_least_one_and_capped(int stored, int expected)
    {
        Assert.Equal(expected, ConfigNormalizer.FirstRepayment(stored));
    }

    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 1)]
    [InlineData(5, 5)]
    [InlineData(int.MaxValue, CustomRules.MaxValue)]
    public void Repayments_is_at_least_one_and_capped(int stored, int expected)
    {
        // Zero repayments would be a run won the moment it starts.
        Assert.Equal(expected, ConfigNormalizer.Repayments(stored));
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]         // 0 = no time limit, a legal setting
    [InlineData(30, 30)]
    [InlineData(int.MaxValue, CustomRules.MaxValue)]
    public void TimeLimitMinutes_is_at_least_zero_and_capped(int stored, int expected)
    {
        Assert.Equal(expected, ConfigNormalizer.TimeLimitMinutes(stored));
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-100, 0)]
    [InlineData(0, 0)]         // 0 = surcharge off, a legal setting
    [InlineData(500, 500)]
    [InlineData(int.MaxValue, CustomRules.MaxValue)]
    public void Surcharge_is_at_least_zero_and_capped(int stored, int expected)
    {
        Assert.Equal(expected, ConfigNormalizer.Surcharge(stored));
    }

    [Theory]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    public void SurchargeEvery_is_at_least_one(int stored, int expected)
    {
        Assert.Equal(expected, ConfigNormalizer.SurchargeEvery(stored));
    }

    [Fact]
    public void SurchargeEvery_is_deliberately_not_capped()
    {
        // Only ever a modulus divisor, so a huge value cannot overflow anything; it just
        // means the surcharge never comes due. Pinned so the asymmetry is not "fixed" by
        // accident.
        Assert.Equal(int.MaxValue, ConfigNormalizer.SurchargeEvery(int.MaxValue));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void GrowthAmount_rejects_non_finite_values(double stored)
    {
        // Mono's double->int conversion is not saturating: a non-finite value reaching the
        // cast yields int.MinValue, a permanently satisfied negative target.
        Assert.Equal(ConfigNormalizer.DefaultGrowthAmount, ConfigNormalizer.GrowthAmount(stored));
    }

    [Theory]
    [InlineData(-7d, 0d)]
    [InlineData(-0.0001d, 0d)]
    [InlineData(0d, 0d)]
    [InlineData(1.05d, 1.05d)]
    [InlineData(20d, 20d)]
    [InlineData(1e300, 1e300)]
    public void GrowthAmount_floors_at_zero_and_otherwise_passes_through(double stored, double expected)
    {
        Assert.Equal(expected, ConfigNormalizer.GrowthAmount(stored));
    }

    [Theory]
    [InlineData("Linear", GrowthMode.Linear)]
    [InlineData("Multiplicative", GrowthMode.Multiplicative)]
    [InlineData("Exponential", GrowthMode.Exponential)]
    public void TryMode_accepts_the_canonical_names(string stored, GrowthMode expected)
    {
        Assert.True(ConfigNormalizer.TryMode(stored, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData("exponential", GrowthMode.Exponential)]
    [InlineData("MULTIPLICATIVE", GrowthMode.Multiplicative)]
    [InlineData("lInEaR", GrowthMode.Linear)]
    public void TryMode_is_case_insensitive(string stored, GrowthMode expected)
    {
        // The case-sensitive overload would silently downgrade these to Linear, and the
        // write-back would then erase the spelling that revealed the mistake.
        Assert.True(ConfigNormalizer.TryMode(stored, out var mode));
        Assert.Equal(expected, mode);
    }

    [Theory]
    [InlineData("7")]          // TryParse alone accepts any numeric string
    [InlineData("-1")]
    [InlineData("banana")]
    [InlineData("")]
    [InlineData(null)]
    public void TryMode_reports_failure_and_falls_back_to_Linear(string stored)
    {
        Assert.False(ConfigNormalizer.TryMode(stored, out var mode));
        Assert.Equal(GrowthMode.Linear, mode);
    }

    [Fact]
    public void Normalised_settings_never_produce_a_non_positive_target()
    {
        // The composition is what matters: normalise, hand the result to CustomRules, and
        // walk a full run. This is the property the whole restructure exists to guarantee.
        var amounts = new[]
        {
            0d, 0.5d, 1d, 1.05d, 20d, 1e9, 1e300,
            double.NaN, double.PositiveInfinity, double.NegativeInfinity, -7d,
        };
        var firsts = new[] { int.MinValue, -5, 0, 1, 10, int.MaxValue };
        var everies = new[] { int.MinValue, 0, 1, 5 };
        var charges = new[] { int.MinValue, -1, 0, 500, int.MaxValue };
        var modes = new[] { GrowthMode.Linear, GrowthMode.Multiplicative, GrowthMode.Exponential };

        var worst = int.MaxValue;

        foreach (var mode in modes)
        foreach (var amount in amounts)
        foreach (var first in firsts)
        foreach (var every in everies)
        foreach (var charge in charges)
        {
            var rules = new CustomRules
            {
                FirstRepayment = ConfigNormalizer.FirstRepayment(first),
                Mode = mode,
                GrowthAmount = ConfigNormalizer.GrowthAmount(amount),
                Surcharge = ConfigNormalizer.Surcharge(charge),
                SurchargeEvery = ConfigNormalizer.SurchargeEvery(every),
            };

            var current = rules.FirstRepayment;
            if (current < worst) worst = current;

            for (var i = 1; i <= 12; i++)
            {
                current = rules.NextTargetCount(current, i);
                if (current < worst) worst = current;
                Assert.InRange(current, 1, CustomRules.MaxValue);
            }
        }

        Assert.Equal(1, worst);
    }
}
