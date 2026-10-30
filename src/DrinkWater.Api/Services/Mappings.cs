using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;

namespace DrinkWater.Api.Services;

public static class Mappings
{
    public static UserProfileDto ToProfile(this User u) => new(
        u.Id, u.Email, u.FullName, u.Role,
        u.TargetWaterMl, u.MorningTargetMl, u.AfternoonTargetMl, u.EveningTargetMl,
        u.PartnerId, u.AvatarUrl, u.RequirePhoto,
        u.ReminderEnabled, u.ReminderStyle, u.ReminderIntervalMinutes, u.ReminderMl,
        u.ReminderStartHour, u.ReminderEndHour, u.CoverUrl);

    /// <summary>Tên hiển thị: biệt hiệu nếu có, không thì tên thật.</summary>
    public static string DisplayName(this string fullName, string? nickname) =>
        string.IsNullOrWhiteSpace(nickname) ? fullName : nickname;
}
