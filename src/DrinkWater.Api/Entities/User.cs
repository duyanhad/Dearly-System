namespace DrinkWater.Api.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.User;
    public int TargetWaterMl { get; set; } = 2000;
    public int MorningTargetMl { get; set; } = 700;
    public int AfternoonTargetMl { get; set; } = 800;
    public int EveningTargetMl { get; set; } = 500;
    public string? FCMToken { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsLocked { get; set; }

    /// <summary>Bắt buộc chụp ảnh minh chứng mỗi lần thêm nước.</summary>
    public bool RequirePhoto { get; set; } = true;

    /// <summary>Nhắc uống nước tự động: bật/tắt, kiểu thông báo, khoảng cách và số ml mỗi lần.</summary>
    public bool ReminderEnabled { get; set; }
    public string ReminderStyle { get; set; } = ReminderStyles.Normal;
    public int ReminderIntervalMinutes { get; set; } = 120;
    public int ReminderMl { get; set; } = 200;
    /// <summary>Khung giờ nhắc trong ngày (giờ địa phương). End = 24 nghĩa là tới hết ngày.</summary>
    public int ReminderStartHour { get; set; } = 7;
    public int ReminderEndHour { get; set; } = 23;

    /// <summary>Người duyệt ảnh minh chứng của tôi. Null = không cần duyệt.</summary>
    public Guid? PartnerId { get; set; }
    /// <summary>Người tôi xin làm người duyệt ảnh, đang chờ họ đồng ý. Null = không có lời mời nào.</summary>
    public Guid? PendingPartnerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Mốc "chốt ngày" của giấc ngủ: chỉ các giấc sau mốc này mới tính vào % hôm nay.</summary>
    public DateTime? SleepDayStartAt { get; set; }
    /// <summary>Ảnh bìa trang cá nhân: đường dẫn upload, hoặc "preset:<id>" cho nền dựng sẵn.</summary>
    public string? CoverUrl { get; set; }

    /// <summary>Đổi giá trị này để vô hiệu hoá mọi token đã phát (khoá, đổi quyền, đăng xuất).</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>"Giọt" — tiền kiếm được khi hoàn thành mục tiêu ngày, dùng để mở kỷ nguyên / nâng cấp.</summary>
    public int Coins { get; set; }

    public List<Follow> Following { get; set; } = [];
    public List<Follow> Followers { get; set; } = [];
}

