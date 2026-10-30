namespace DrinkWater.Api.Services;

/// <summary>Every "today", period split and streak calculation goes through here so the
/// app uses one time zone instead of the server's clock (which is UTC on most hosts).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateTime LocalNow { get; }
    TimeOnly LocalTimeNow { get; }
    DateOnly TodayLocal { get; }
    DateOnly ToLocalDate(DateTime utc);
    DateTime LocalToUtc(DateOnly date, TimeOnly time);
}

public sealed class AppClock : IClock
{
    private readonly TimeZoneInfo _zone;

    public AppClock(IConfiguration config)
    {
        var id = config["App:TimeZone"] ?? "Asia/Ho_Chi_Minh";
        _zone = TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _zone);
    public TimeOnly LocalTimeNow => TimeOnly.FromDateTime(LocalNow);
    public DateOnly TodayLocal => DateOnly.FromDateTime(LocalNow);

    public DateOnly ToLocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone));

    public DateTime LocalToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time), _zone);
}
