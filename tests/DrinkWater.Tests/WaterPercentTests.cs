using DrinkWater.Api.Services;

namespace DrinkWater.Tests;

public class WaterPercentTests
{
    [Theory]
    [InlineData(0, 2000, 0)]
    [InlineData(500, 2000, 25)]
    [InlineData(2000, 2000, 100)]
    [InlineData(2500, 2000, 125)]
    [InlineData(500, 0, 0)]
    public void Percent_is_drank_over_target(int drank, int target, int expected)
    {
        Assert.Equal(expected, WaterService.Percent(drank, target));
    }
}
