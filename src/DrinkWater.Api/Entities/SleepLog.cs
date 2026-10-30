namespace DrinkWater.Api.Entities;

/// <summary>Một đêm ngủ đã ghi: giờ đi ngủ, giờ thức và số phút ngủ thực tế.</summary>
public class SleepLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Ngày của buổi sáng thức dậy (một đêm tính cho ngày hôm đó).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Phút tính từ 00:00 của ngày đi ngủ (23:00 = 1380).</summary>
    public int BedMinutes { get; set; }

    /// <summary>Phút tính từ 00:00 của ngày thức dậy (07:00 = 420).</summary>
    public int WakeMinutes { get; set; }

    /// <summary>Số phút đã ngủ (đã cộng 24h nếu ngủ qua nửa đêm).</summary>
    public int Minutes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ảnh minh chứng (partner duyệt) — có thể null nếu người dùng không cần ảnh.</summary>
    public string? PhotoUrl { get; set; }

    /// <summary>Ảnh minh chứng dậy đúng giờ (chụp lúc thức) — tuỳ chọn.</summary>
    public string? WakePhotoUrl { get; set; }

    public string Note { get; set; } = string.Empty;

    /// <summary>pending | approved | rejected.</summary>
    public string Status { get; set; } = "approved";

    public string? RejectReason { get; set; }

    public DateTime? ReviewedAt { get; set; }
}
