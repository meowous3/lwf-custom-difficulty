using LwfCustomDifficulty;
using Xunit;

public class CustomRulesTests
{
    private static CustomRules Vanilla() => new CustomRules
    {
        FirstRepayment = 10, Mode = GrowthMode.Linear, GrowthAmount = 20,
        Surcharge = 500, SurchargeEvery = 5,
    };

    [Fact]
    public void Linear_adds_a_fixed_amount()
    {
        var rules = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Linear, GrowthAmount = 20,
            Surcharge = 0, SurchargeEvery = 5,
        };
        Assert.Equal(30, rules.NextTargetCount(10, 1));
        Assert.Equal(50, rules.NextTargetCount(30, 2));
    }

    [Fact]
    public void Multiplicative_scales_the_current_value()
    {
        var rules = new CustomRules
        {
            FirstRepayment = 100, Mode = GrowthMode.Multiplicative, GrowthAmount = 1.5,
            Surcharge = 0, SurchargeEvery = 5,
        };
        Assert.Equal(150, rules.NextTargetCount(100, 1));
        Assert.Equal(225, rules.NextTargetCount(150, 2));
    }

    [Fact]
    public void Exponential_compounds_the_step_by_the_growth_amount()
    {
        var rules = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Exponential, GrowthAmount = 1.1,
            Surcharge = 0, SurchargeEvery = 5,
        };
        Assert.Equal(21, rules.NextTargetCount(10, 1));    // step 10 * 1.1^1 = 11.0
        Assert.Equal(34, rules.NextTargetCount(21, 2));    // step 10 * 1.1^2 = 12.1  -> 33.1
        Assert.Equal(48, rules.NextTargetCount(34, 3));    // step 10 * 1.1^3 = 13.31 -> 47.31
        Assert.Equal(63, rules.NextTargetCount(48, 4));    // step 10 * 1.1^4 = 14.64 -> 62.64
    }

    [Fact]
    public void Exponential_growth_amount_is_the_acceleration_not_the_step()
    {
        // The distinguishing property of the rework: the same FirstRepayment with a larger
        // GrowthAmount accelerates, rather than merely adding a bigger constant.
        var gentle = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Exponential, GrowthAmount = 1.1,
            Surcharge = 0, SurchargeEvery = 5,
        };
        var steep = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Exponential, GrowthAmount = 1.5,
            Surcharge = 0, SurchargeEvery = 5,
        };

        // They diverge from the very first step: 11.0 against 15.0. The exponent is the
        // index rather than index-1 precisely so this holds — with index-1 both would open
        // at 20, because anything^0 is 1 and GrowthAmount would not reach the first step.
        Assert.Equal(21, gentle.NextTargetCount(10, 1));
        Assert.Equal(25, steep.NextTargetCount(10, 1));

        Assert.Equal(34, gentle.NextTargetCount(21, 2));   // step 12.1
        Assert.Equal(48, steep.NextTargetCount(25, 2));    // step 22.5  -> 47.5
        Assert.Equal(82, steep.NextTargetCount(48, 3));    // step 33.75 -> 81.75
        Assert.Equal(133, steep.NextTargetCount(82, 4));   // step 50.625 -> 132.625
    }

    [Fact]
    public void Exponential_holds_flat_when_the_growth_amount_is_zero()
    {
        // The exponent is 1-based, so Pow(0, n) is 0 from the first step onward and the
        // never-decrease guard pins the target where it started. Zero acceleration means no
        // growth at all, not one free step: that only differed while the exponent was
        // index-1, where Pow(0, 0) = 1 let the opening step through.
        var rules = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Exponential, GrowthAmount = 0,
            Surcharge = 0, SurchargeEvery = 5,
        };
        Assert.Equal(10, rules.NextTargetCount(10, 1));
        Assert.Equal(10, rules.NextTargetCount(10, 2));
        Assert.Equal(10, rules.NextTargetCount(10, 3));
    }

    [Fact]
    public void Exponential_clamps_rather_than_overflowing()
    {
        // Pow overflows to +Infinity long before the exponent runs out; the ceiling has to
        // catch it, because Mono's double->int conversion is not saturating and would yield
        // int.MinValue: a permanently negative, permanently satisfied target.
        var rules = new CustomRules
        {
            FirstRepayment = 1000, Mode = GrowthMode.Exponential, GrowthAmount = 10,
            Surcharge = 0, SurchargeEvery = 5,
        };
        Assert.Equal(CustomRules.MaxValue, rules.NextTargetCount(1, 400));
    }

    [Fact]
    public void Surcharge_applies_on_the_interval_only()
    {
        var rules = Vanilla();
        Assert.Equal(30, rules.NextTargetCount(10, 1));       // no surcharge
        Assert.Equal(530, rules.NextTargetCount(10, 5));      // +20 +500
        Assert.Equal(530, rules.NextTargetCount(10, 10));
    }

    [Fact]
    public void Surcharge_of_zero_never_applies()
    {
        var rules = Vanilla();
        rules.Surcharge = 0;
        Assert.Equal(30, rules.NextTargetCount(10, 5));
    }

    [Fact]
    public void Result_is_clamped_to_max()
    {
        var rules = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Multiplicative, GrowthAmount = 1000,
            Surcharge = 0, SurchargeEvery = 5,
        };
        Assert.Equal(536870911, CustomRules.MaxValue);
        Assert.Equal(CustomRules.MaxValue, rules.NextTargetCount(1_000_000, 1));   // 1e9 > cap
    }

    [Fact]
    public void Surcharge_cannot_push_past_max()
    {
        var rules = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Linear, GrowthAmount = 0,
            Surcharge = 500, SurchargeEvery = 5,
        };
        Assert.Equal(CustomRules.MaxValue, rules.NextTargetCount(CustomRules.MaxValue, 5));
    }

    [Fact]
    public void NaN_growth_holds_the_current_value()
    {
        var rules = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Multiplicative, GrowthAmount = double.NaN,
            Surcharge = 0, SurchargeEvery = 5,
        };
        Assert.Equal(100, rules.NextTargetCount(100, 1));
    }

    [Fact]
    public void Multiplier_just_above_one_still_makes_progress()
    {
        var rules = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Multiplicative, GrowthAmount = 1.05,
            Surcharge = 0, SurchargeEvery = 5,
        };
        Assert.Equal(11, rules.NextTargetCount(10, 1));   // truncation would pin this at 10
    }

    [Fact]
    public void Result_never_decreases()
    {
        var rules = new CustomRules
        {
            FirstRepayment = 10, Mode = GrowthMode.Multiplicative, GrowthAmount = 0.5,
            Surcharge = 0, SurchargeEvery = 5,
        };
        Assert.Equal(100, rules.NextTargetCount(100, 1));
    }
}
