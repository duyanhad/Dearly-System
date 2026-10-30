namespace DrinkWater.Api.Dtos;

/// <summary>Cài đặt nhắc cho một nhu cầu (uống thuốc / bữa ăn / giấc ngủ).</summary>
public record HabitSettingDto(
    string Kind,
    bool Enabled,
    List<string> Times,
    int TargetValue,
    int RemindBeforeMin,
    string Note,
    bool AlarmEnabled,
    int SnoozeMinutes,
    string AlarmMode,
    int AlarmRepeatPerRound,
    int AlarmRounds,
    int AlarmIntervalMin,
    bool LockUntilSettings,
    string? ImageUrl,
    string? CutoutUrl,
    string? Direction,
    int? StartValue,
    DateOnly? StartDate);

public record HabitSettingRequest(
    bool Enabled,
    List<string>? Times,
    int TargetValue,
    int RemindBeforeMin,
    string? Note,
    bool? AlarmEnabled,
    int? SnoozeMinutes,
    string? AlarmMode,
    int? AlarmRepeatPerRound,
    int? AlarmRounds,
    int? AlarmIntervalMin,
    bool? LockUntilSettings,
    string? Direction);
