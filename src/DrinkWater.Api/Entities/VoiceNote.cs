namespace DrinkWater.Api.Entities;

/// <summary>
/// Lời nhắn thoại hẹn giờ gửi cho partner/bạn bè. Tới giờ hẹn thì hệ thống kêu lặp
/// (push + realtime) khoảng 3 lần/phút cho tới khi người nhận bấm "đã nghe".
/// </summary>
public class VoiceNote
{
    public int Id { get; set; }
    public Guid SenderId { get; set; }
    public Guid ReceiverId { get; set; }
    public string AudioUrl { get; set; } = string.Empty;
    public int DurationMs { get; set; }

    /// <summary>Thời điểm bắt đầu kêu (UTC).</summary>
    public DateTime ScheduledAtUtc { get; set; }

    /// <summary>Người nhận đã nghe lúc nào (UTC) — nghe rồi thì thôi kêu.</summary>
    public DateTime? ListenedAtUtc { get; set; }

    /// <summary>Lần kêu gần nhất (UTC) để giãn nhịp kêu.</summary>
    public DateTime? LastRungAtUtc { get; set; }

    public int RingCount { get; set; }

    /// <summary>Người gửi huỷ hoặc hệ thống dừng sau khi kêu quá lâu.</summary>
    public bool Cancelled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Sender { get; set; } = null!;
    public User Receiver { get; set; } = null!;
}