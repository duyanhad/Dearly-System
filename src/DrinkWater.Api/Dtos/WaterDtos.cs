namespace DrinkWater.Api.Dtos;

public record PeriodProgressDto(string Period, int DrankMl, int TargetMl, int Percent);

public record WaterLogDto(
    int Id, int AmountMl, string Period, string? ImageUrl, string? Note, string Status, DateTime LogTime);

public record TodayDto(
    DateOnly Date,
    int TotalDrankMl,
    int TargetMl,
    int Percent,
    bool IsCompleted,
    int StreakCount,
    int PendingMl,
    int DeductedMl,
    List<PeriodProgressDto> Periods,
    List<WaterLogDto> Logs);

public record CalendarDayDto(DateOnly Date, int TotalDrankMl, int TargetMl, bool IsCompleted, int StreakCount);

public record CalendarDto(int Year, int Month, int CurrentStreak, List<CalendarDayDto> Days);

public record DayDto(DateOnly Date, int TotalDrankMl, int TargetMl, bool IsCompleted, List<WaterLogDto> Logs);

// Plain settable class: [FromForm] binds it reliably, record constructors do not.
public class AddWaterLogRequest
{
    [System.ComponentModel.DataAnnotations.Range(1, 5000)]
    public int AmountMl { get; set; }

    public string? Period { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(255)]
    public string? Note { get; set; }
}
