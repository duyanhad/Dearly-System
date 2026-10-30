using DrinkWater.Api.Services;

namespace DrinkWater.Tests.Integration;

/// <summary>Fixed clock so "today", period split and streak are deterministic in tests.</summary>
public sealed class FakeClock : IClock
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    /// <summary>2026-01-10 10:00 in Vietnam.</summary>
    public DateTime UtcNow { get; set; } = new(2026, 1, 10, 3, 0, 0, DateTimeKind.Utc);

    public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, Zone);
    public TimeOnly LocalTimeNow => TimeOnly.FromDateTime(LocalNow);
    public DateOnly TodayLocal => DateOnly.FromDateTime(LocalNow);

    public DateOnly ToLocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone));

    public DateTime LocalToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time), Zone);
}
