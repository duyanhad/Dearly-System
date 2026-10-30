namespace DrinkWater.Api.Entities;

/// <summary>
/// Cài đặt nhắc nhở cho một nhu cầu ngoài nước (uống thuốc / bữa ăn / giấc ngủ).
/// Dùng chung một bảng để sau này thêm nhu cầu mới chỉ cần thêm <see cref="Kind"/>.
/// </summary>
public class HabitSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>medicine | meal | sleep.</summary>
    public string Kind { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    /// <summary>Các mốc giờ "HH:mm" cách nhau dấu phẩy (thuốc: lần uống; ăn: giờ bữa; ngủ: giờ ngủ, giờ thức).</summary>
    public string Times { get; set; } = string.Empty;

    /// <summary>Thuốc: số lần/ngày · Ăn: số bữa/ngày · Ngủ: số phút ngủ mục tiêu.</summary>
    public int TargetValue { get; set; }

    /// <summary>Nhắc trước bao nhiêu phút.</summary>
    public int RemindBeforeMin { get; set; }

    public string Note { get; set; } = string.Empty;

    /// <summary>Bật báo thức đánh thức (chỉ dùng cho giấc ngủ).</summary>
    public bool AlarmEnabled { get; set; }

    /// <summary>Báo lại sau bao nhiêu phút khi bấm "ngủ thêm" (0 = tắt).</summary>
    public int SnoozeMinutes { get; set; } = 10;

    /// <summary>Kiểu báo thức: gentle (1 lần) · insist (vài lần) · crazy (điên cuồng, nhiều vòng).</summary>
    public string AlarmMode { get; set; } = "gentle";

    /// <summary>Số lần kêu trong một vòng.</summary>
    public int AlarmRepeatPerRound { get; set; } = 3;

    /// <summary>Số vòng phải chạy (giới hạn).</summary>
    public int AlarmRounds { get; set; } = 3;

    /// <summary>Giãn cách giữa các lần kêu (phút).</summary>
    public int AlarmIntervalMin { get; set; } = 1;

    /// <summary>Chống tắt: chỉ tắt được khi vào Cài đặt, còn không phải chạy đủ số vòng.</summary>
    public bool LockUntilSettings { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ảnh "vật thể" của nhu cầu (đĩa ăn, gối…) do người dùng tải lên.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Bản đã tách nền (server xử lý) để làm hình chính; đủ chỉ tiêu thì hiện ảnh gốc.</summary>
    public string? CutoutUrl { get; set; }

    /// <summary>Cân nặng: mục tiêu là giảm cân ("down") hay tăng cân ("up"). Null = tự suy từ mục tiêu.</summary>
    public string? Direction { get; set; }

    /// <summary>Cân nặng: mốc ban đầu cố định (gam) — chốt ở lần cân đầu tiên, dùng để tính % tiến trình.</summary>
    public int? StartValue { get; set; }

    /// <summary>Ngày chốt mốc ban đầu.</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>Ba nhu cầu đang hỗ trợ (mở rộng thêm ở đây là đủ).</summary>
    /// <summary>Các nhu cầu có ghi nhận theo lần (không gồm sleep).</summary>
    public static readonly string[] LogKinds = ["medicine", "meal", "study", "weight"];

    public static readonly string[] Kinds = ["medicine", "meal", "sleep", "study", "weight"];

    public static HabitSetting Default(Guid userId, string kind) => new()
    {
        UserId = userId,
        Kind = kind,
        Enabled = true,
        // weight: mục tiêu tính bằng gam (60000 = 60,0 kg)
        TargetValue = kind switch { "medicine" => 3, "meal" => 3, "study" => 2, "weight" => 60000, _ => 480 },
        RemindBeforeMin = kind == "sleep" ? 30 : 10,
        AlarmEnabled = kind == "sleep",
        SnoozeMinutes = 10,
        AlarmMode = "gentle",
        AlarmRepeatPerRound = 3,
        AlarmRounds = 3,
        AlarmIntervalMin = 1,
        LockUntilSettings = false,
        Times = kind switch { "medicine" => "08:00,13:00,20:00", "meal" => "07:00,12:00,19:00", "study" => "09:00,20:00", "weight" => "07:00", _ => "23:00,07:00" },
    };
}
