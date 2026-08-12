using System.Globalization;
using LwfCustomDifficulty;
using Xunit;

public class NumericTextTests
{
    // TMP_InputField validates each typed character against the running culture's decimal
    // separator, so a panel that formatted and parsed in one culture while the field
    // validated in another made fractional input impossible: the field refused the
    // separator the parser wanted and the parser refused the separator the field allowed.
    private static readonly CultureInfo Comma = new CultureInfo("de-DE");
    private static readonly CultureInfo Dot = new CultureInfo("en-US");

    [Fact]
    public void Fractional_text_round_trips_in_a_comma_decimal_culture()
    {
        var rendered = NumericText.Format(1.5d, Comma);
        Assert.Equal("1,5", rendered);
        Assert.Equal(1.5d, NumericText.ParseDouble(rendered, fallback: 20d, Comma));
    }

    [Fact]
    public void Fractional_text_round_trips_in_a_dot_decimal_culture()
    {
        var rendered = NumericText.Format(1.5d, Dot);
        Assert.Equal("1.5", rendered);
        Assert.Equal(1.5d, NumericText.ParseDouble(rendered, fallback: 20d, Dot));
    }

    [Fact]
    public void The_other_cultures_separator_is_not_silently_accepted()
    {
        // The field will not let it be typed either; what matters is that it does not parse
        // as some other number.
        Assert.Equal(20d, NumericText.ParseDouble("1.5", fallback: 20d, Comma));
    }

    [Fact]
    public void Whole_numbers_render_without_group_separators()
    {
        Assert.Equal("536870911", NumericText.Format(536870911, Comma));
        Assert.Equal("536870911", NumericText.Format(536870911, Dot));
    }

    [Fact]
    public void Very_small_and_very_large_values_avoid_exponent_notation()
    {
        // The field's character validation accepts digits, one sign and one separator; an
        // 'E' would make the rendered value un-reparseable.
        Assert.DoesNotContain("E", NumericText.Format(0.0000001d, Dot));
        Assert.DoesNotContain("E", NumericText.Format(1e20d, Dot));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-5", -5)]
    [InlineData("536870911", 536870911)]
    // Out of int range: clamped to the end it ran off, never returned as the fallback.
    // Falling back would leave the typed number on screen and the config untouched, and
    // ConfigNormalizer.Clamp would never see it.
    [InlineData("9999999999", int.MaxValue)]
    [InlineData("-9999999999", int.MinValue)]
    [InlineData("99999999999999999999999999", int.MaxValue)]
    [InlineData("2147483648", int.MaxValue)]
    public void Whole_number_text_clamps_instead_of_falling_back(string typed, int expected)
    {
        Assert.Equal(expected, NumericText.ParseInt(typed, fallback: 7, Dot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Text_that_names_no_number_keeps_the_stored_value(string typed)
    {
        Assert.Equal(7, NumericText.ParseInt(typed, fallback: 7, Dot));
        Assert.Equal(20d, NumericText.ParseDouble(typed, fallback: 20d, Dot));
    }

    [Fact]
    public void An_overflowing_whole_number_survives_normalisation_as_the_cap()
    {
        // The end-to-end shape of the guarantee: typed text -> parse -> normalise -> cap.
        var parsed = NumericText.ParseInt("9999999999", fallback: 30, Dot);
        Assert.Equal(CustomRules.MaxValue, ConfigNormalizer.TimeLimitMinutes(parsed));
    }

    [Fact]
    public void Not_a_number_does_not_truncate_to_int_MinValue()
    {
        // Mono's double->int conversion is not saturating, so NaN reaching the cast would
        // become int.MinValue: a negative, permanently satisfied target.
        Assert.Equal(7, NumericText.ParseInt("NaN", fallback: 7, Dot));
    }
}
