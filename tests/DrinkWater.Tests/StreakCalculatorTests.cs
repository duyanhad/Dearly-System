using DrinkWater.Api.Services;

namespace DrinkWater.Tests;

public class StreakCalculatorTests
{
    private static DateOnly D(int day) => new(2026, 1, day);

    [Fact]
    public void Consecutive_completed_days_increase_the_streak()
    {
        var result = StreakCalculator.Compute(
            [(D(1), true), (D(2), true), (D(3), true)], null, 0);

        Assert.Equal([1, 2, 3], result.Select(r => r.Streak));
    }

    [Fact]
    public void A_missing_day_breaks_the_chain()
    {
        // Jan 2 has no row at all: Jan 1 and Jan 3 are not consecutive.
        var result = StreakCalculator.Compute(
            [(D(1), true), (D(3), true), (D(4), true)], null, 0);

        Assert.Equal([1, 1, 2], result.Select(r => r.Streak));
    }

    [Fact]
    public void A_day_below_target_resets_the_streak()
    {
        var result = StreakCalculator.Compute(
            [(D(1), true), (D(2), false), (D(3), true)], null, 0);

        Assert.Equal([1, 0, 1], result.Select(r => r.Streak));
    }

    [Fact]
    public void An_anchor_day_continues_the_existing_streak()
    {
        var result = StreakCalculator.Compute(
            [(D(2), true), (D(3), true)], D(1), 5);

        Assert.Equal([6, 7], result.Select(r => r.Streak));
    }

    [Fact]
    public void A_failed_anchor_day_starts_a_new_streak()
    {
        var result = StreakCalculator.Compute([(D(2), true)], D(1), 0);

        Assert.Equal([1], result.Select(r => r.Streak));
    }

    [Fact]
    public void Empty_input_is_empty_output()
    {
        Assert.Empty(StreakCalculator.Compute([], null, 3));
    }
}
