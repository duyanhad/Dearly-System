using DrinkWater.Api.Entities;

namespace DrinkWater.Tests;

public class PeriodTests
{
    [Theory]
    [InlineData(0, 0, Periods.Morning)]
    [InlineData(11, 59, Periods.Morning)]
    [InlineData(12, 0, Periods.Afternoon)]
    [InlineData(17, 59, Periods.Afternoon)]
    [InlineData(18, 0, Periods.Evening)]
    [InlineData(23, 59, Periods.Evening)]
    public void FromLocalTime_splits_the_day_in_three_periods(int hour, int minute, string expected)
    {
        Assert.Equal(expected, Periods.FromLocalTime(new TimeOnly(hour, minute)));
    }
}
