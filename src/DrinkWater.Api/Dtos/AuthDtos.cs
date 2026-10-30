using System.ComponentModel.DataAnnotations;

namespace DrinkWater.Api.Dtos;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(100)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [Required, MaxLength(100)] string FullName);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record UserProfileDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    int TargetWaterMl,
    int MorningTargetMl,
    int AfternoonTargetMl,
    int EveningTargetMl,
    Guid? PartnerId,
    string? AvatarUrl,
    bool RequirePhoto = true,
    bool ReminderEnabled = false,
    string ReminderStyle = "normal",
    int ReminderIntervalMinutes = 120,
    int ReminderMl = 200,
    int ReminderStartHour = 7,
    int ReminderEndHour = 23,
    string? CoverUrl = null);

public record AuthResponse(string Token, DateTime ExpiresAtUtc, UserProfileDto User);

public record UpdateTargetsRequest(
    [Range(100, 20000)] int TargetWaterMl,
    [Range(0, 20000)] int MorningTargetMl,
    [Range(0, 20000)] int AfternoonTargetMl,
    [Range(0, 20000)] int EveningTargetMl);

public record FcmTokenRequest([Required] string Token);

public record UpdateProfileRequest([Required, MaxLength(100)] string FullName);

/// <summary>Tự đổi mật khẩu khi còn nhớ mật khẩu hiện tại (dùng được cả khi chưa đăng nhập).</summary>
public record ChangePasswordRequest(
    [Required, EmailAddress, MaxLength(100)] string Email,
    [Required] string CurrentPassword,
    [Required, MinLength(8), MaxLength(100)] string NewPassword);

/// <summary>Admin đặt lại mật khẩu; để trống NewPassword thì server sinh ngẫu nhiên.</summary>
public record AdminResetPasswordRequest([MaxLength(100)] string? NewPassword);

public record AdminResetPasswordResult(string Email, string Password);

/// <summary>Tuỳ chọn cá nhân; gửi field nào thì đổi field đó.</summary>
public record UpdateSettingsRequest(
    bool? RequirePhoto,
    bool? ReminderEnabled,
    string? ReminderStyle,
    int? ReminderIntervalMinutes,
    int? ReminderMl,
    int? ReminderStartHour,
    int? ReminderEndHour);

public record UserSummaryDto(
    Guid Id, string Email, string FullName, bool IsFollowing, string Status, bool IsMyPartner,
    string? Nickname, string? AvatarUrl);

public record UserStatsDto(int TotalLogs, int TotalMl, int CurrentStreak, int CompletedDays, DateTime JoinedAt);
