namespace DrinkWater.Api.Entities;

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

public static class Periods
{
    public const string Morning = "Morning";
    public const string Afternoon = "Afternoon";
    public const string Evening = "Evening";

    public static readonly string[] All = [Morning, Afternoon, Evening];

    public static string FromLocalTime(TimeOnly time) => time.Hour switch
    {
        < 12 => Morning,
        < 18 => Afternoon,
        _ => Evening
    };
}

public static class NotificationTypes
{
    public const string Reward = "Reward";
    public const string Nudge = "Nudge";
    public const string Broadcast = "Broadcast";
    public const string GoalAchieved = "GoalAchieved";
    public const string Deduct = "Deduct";
    /// <summary>Lời mời kết bạn / chấp nhận kết bạn.</summary>
    public const string Friend = "Friend";
    /// <summary>Được chọn làm partner duyệt ảnh.</summary>
    public const string Partner = "Partner";
    /// <summary>Phiếu minh chứng mới chờ duyệt.</summary>
    public const string Proof = "Proof";
    /// <summary>Người nhận phản hồi lại một thông báo (vd: phản hồi ảnh đã duyệt).</summary>
    public const string Reply = "Reply";
    /// <summary>Lời nhắn thoại hẹn giờ (đã hẹn gửi hoặc đang kêu).</summary>
    public const string VoiceNote = "VoiceNote";
}

public static class ReminderStyles
{
    /// <summary>Nhẹ nhàng: nhắc thưa, câu từ mềm.</summary>
    public const string Gentle = "gentle";
    /// <summary>Trung bình: mặc định.</summary>
    public const string Normal = "normal";
    /// <summary>Ác quỷ: nhắc dồn dập, câu từ gay gắt.</summary>
    public const string Demon = "demon";

    public static readonly string[] All = [Gentle, Normal, Demon];

    public static bool IsValid(string? style) => style is not null && All.Contains(style);
}

/// <summary>Nền dựng sẵn cho ảnh bìa trang cá nhân (không cần tải ảnh).</summary>
public static class CoverPresets
{
    public const string Ocean = "ocean";
    public const string Sunset = "sunset";
    public const string Night = "night";
    public const string Mint = "mint";
    public const string Peach = "peach";

    public static readonly string[] All = [Ocean, Sunset, Night, Mint, Peach];

    public static bool IsValid(string? preset) => preset is not null && All.Contains(preset);
}

public static class LogLevels
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Error = "Error";
}

public static class FollowStatuses
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
}

public static class LogStatuses
{
    public const string Approved = "Approved";
    public const string Pending = "Pending";
    public const string Rejected = "Rejected";
    /// <summary>Log không có ảnh: vẫn tính vào tiến trình nhưng không có bằng chứng.</summary>
    public const string Unverified = "Unverified";
    /// <summary>Partner trừ nước: AmountMl âm nên cộng vào tổng là trừ ra.</summary>
    public const string Deducted = "Deducted";

    public static readonly string[] Counted = [Approved, Unverified, Deducted];
}
