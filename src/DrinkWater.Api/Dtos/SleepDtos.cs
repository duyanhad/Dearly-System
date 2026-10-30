namespace DrinkWater.Api.Dtos;

/// <summary>Một đêm ngủ (phía client dùng giờ "HH:mm").</summary>
public record SleepLogDto(
    Guid Id,
    DateOnly Date,
    string BedTime,
    string WakeTime,
    int Minutes,
    string? PhotoUrl,
    string? WakePhotoUrl,
    string Note,
    string Status,
    string? RejectReason);

public record SleepSummaryDto(
    int GoalMinutes,
    int AverageMinutes,
    int BestMinutes,
    int NightsLogged,
    int NightsHitGoal,
    int StreakNights,
    List<SleepLogDto> Logs);

public record SleepReviewRequest(bool Approve, string? Reason);
