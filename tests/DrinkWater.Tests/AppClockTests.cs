using DrinkWater.Api.Services;
using Microsoft.Extensions.Configuration;

namespace DrinkWater.Tests;

public class AppClockTests
{
    private static IClock Clock() => new AppClock(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["App:TimeZone"] = "Asia/Ho_Chi_Minh" })
        .Build());

    [Fact]
    public void Utc_evening_is_next_day_in_vietnam()
    {
        var clock = Clock();

        // 17:30 UTC = 00:30 the next day in UTC+7
        Assert.Equal(new DateOnly(2026, 1, 2), clock.ToLocalDate(new DateTime(2026, 1, 1, 17, 30, 0)));
        Assert.Equal(new DateOnly(2026, 1, 1), clock.ToLocalDate(new DateTime(2026, 1, 1, 16, 59, 0)));
    }

    [Fact]
    public void Local_time_converts_back_to_utc()
    {
        var clock = Clock();

        var utc = clock.LocalToUtc(new DateOnly(2026, 1, 2), new TimeOnly(0, 30));

        Assert.Equal(new DateTime(2026, 1, 1, 17, 30, 0), utc);
        Assert.Equal(new DateOnly(2026, 1, 2), clock.ToLocalDate(utc));
    }
}
