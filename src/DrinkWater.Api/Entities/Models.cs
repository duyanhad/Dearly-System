namespace DrinkWater.Api.Entities;

public class Follow
{
    public Guid FollowerId { get; set; }
    public Guid FollowingId { get; set; }

    /// <summary>Pending = lời mời chờ chủ tài khoản chấp nhận; chỉ Accepted mới thấy bảng tin.</summary>
    public string Status { get; set; } = FollowStatuses.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Follower { get; set; } = null!;
    public User Following { get; set; } = null!;
}

/// <summary>Biệt hiệu một người đặt cho người khác; độc lập với quan hệ follow.</summary>
public class FriendNickname
{
    public Guid OwnerId { get; set; }
    public Guid TargetId { get; set; }
    public string Nickname { get; set; } = string.Empty;
}

public class CupPreset
{
    public int Id { get; set; }
    /// <summary>null = cốc mẫu do admin tạo (catalog chung cho mọi người dùng).</summary>
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int VolumeMl { get; set; }

    /// <summary>Ảnh cốc do người dùng tải lên; dùng làm texture cho ly 3D.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Ảnh đã tách nền (PNG trong suốt) do dịch vụ rembg tạo; null = client tự tách.</summary>
    public string? CutoutUrl { get; set; }

    /// <summary>Cốc đang hiển thị trên dashboard 3D (mỗi user chỉ 1).</summary>
    public bool IsDefault { get; set; }

    public User User { get; set; } = null!;
}

public class WaterLog
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public int AmountMl { get; set; }
    public string Period { get; set; } = Periods.Morning;
    public string? ImageUrl { get; set; }
    public string? Note { get; set; }

    /// <summary>Approved/Unverified mới được cộng vào tiến trình; Pending chờ partner duyệt.</summary>
    public string Status { get; set; } = LogStatuses.Unverified;

    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime LogTime { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

public class DailySummary
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public int TotalDrankMl { get; set; }
    public int TargetMl { get; set; }
    public bool IsCompleted { get; set; }
    public int StreakCount { get; set; }

    /// <summary>Đã thưởng "giọt" cho ngày này chưa (tránh cộng 2 lần khi sửa log).</summary>
    public bool CoinsAwarded { get; set; }

    public User User { get; set; } = null!;
}

public class Notification
{
    public int Id { get; set; }
    public Guid? SenderId { get; set; }
    public Guid? ReceiverId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Type { get; set; } = NotificationTypes.Broadcast;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SystemLog
{
    public int Id { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string LogLevel { get; set; } = LogLevels.Info;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}




