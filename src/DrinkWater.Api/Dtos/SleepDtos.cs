namespace DrinkWater.Api.Dtos;

/// <summary>Một giấc ngủ (phía client dùng giờ "HH:mm").</summary>
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
    string? RejectReason,
    DateTime? StartedAtUtc = null,
    DateTime? EndedAtUtc = null,
    int PlannedMinutes = 0);

public record SleepSummaryDto(
    int GoalMinutes,
    int AverageMinutes,
    int BestMinutes,
    int NightsLogged,
    int NightsHitGoal,
    int StreakNights,
    List<SleepLogDto> Logs,
    /// <summary>Tổng số phút đã ngủ trong hôm nay (cộng mọi giấc, kể cả giấc đang chạy).</summary>
    int MinutesToday = 0,
    /// <summary>Phần trăm hoàn thành mục tiêu ngủ của hôm nay (0-100).</summary>
    int PercentToday = 0);

/// <summary>Giấc đang ngủ dở (nếu có) để client chạy bộ đếm.</summary>
public record SleepRunningDto(Guid Id, DateTime StartedAtUtc, int PlannedMinutes, string BedTime);

public record SleepReviewRequest(bool Approve, string? Reason);
