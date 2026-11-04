namespace DrinkWater.Api.Entities;

/// <summary>
/// Nhiệm vụ partner giao cho nhau: người giao đặt tên + hạn báo cáo, người nhận báo cáo
/// (ảnh / voice / tin nhắn, chọn nhiều) rồi người giao duyệt đạt / chưa đạt.
/// </summary>
public class Duty
{
    public int Id { get; set; }
    public Guid AssignerId { get; set; }
    public Guid AssigneeId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Mô tả thêm của người giao (có thể rỗng).</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Giờ bắt đầu làm nhiệm vụ (UTC) — người giao đặt.</summary>
    public DateTime StartsAtUtc { get; set; }

    /// <summary>Nhắc trước giờ bắt đầu bao nhiêu phút (0 = không nhắc sớm).</summary>
    public int RemindBeforeMin { get; set; }
    /// <summary>Hạn báo cáo (UTC).</summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>
    /// Người nhận bấm "đã nhận nhiệm vụ" lúc nào. Chưa nhận thì hệ thống kêu lặp cho tới khi nhận.
    /// </summary>
    public DateTime? AcceptedAtUtc { get; set; }
    /// <summary>Lần kêu gần nhất (UTC) để giãn nhịp nhắc.</summary>
    public DateTime? LastRungAtUtc { get; set; }
    /// <summary>Số lần đã kêu "nhận nhiệm vụ" (chặn spam tối đa 60 lần).</summary>
    public int RingCount { get; set; }

    /// <summary>open | submitted | approved | rejected.</summary>
    public string Status { get; set; } = DutyStatuses.Open;

    /// <summary>Số lần đã báo cáo (bị trả lại thì tăng).</summary>
    public int Attempt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    /// <summary>Lời nhắn khi duyệt chưa đạt.</summary>
    public string? ReviewNote { get; set; }

    /// <summary>Đã nhắc "sắp tới hạn" / "sắp tới giờ bắt đầu" / "quá hạn" chưa (mỗi mốc một lần).</summary>
    public DateTime? StartNotifiedAtUtc { get; set; }
    public DateTime? DueSoonNotifiedAtUtc { get; set; }
    public DateTime? OverdueNotifiedAtUtc { get; set; }

    public User Assigner { get; set; } = null!;
    public User Assignee { get; set; } = null!;
    public List<DutyMedia> Media { get; set; } = [];
}

/// <summary>Một tệp/đoạn nội dung đính kèm nhiệm vụ (ảnh, voice hoặc tin nhắn chữ).</summary>
public class DutyMedia
{
    public int Id { get; set; }
    public int DutyId { get; set; }

    /// <summary>assign = nội dung người giao gửi kèm; report = bằng chứng người nhận báo cáo.</summary>
    public string Side { get; set; } = DutySides.Assign;

    /// <summary>image | voice | text.</summary>
    public string Kind { get; set; } = DutyMediaKinds.Text;

    public string? Text { get; set; }
    public string? Url { get; set; }
    public int DurationMs { get; set; }

    /// <summary>Lần báo cáo thứ mấy (chỉ dùng cho side = report).</summary>
    public int Attempt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Duty Duty { get; set; } = null!;
}

public static class DutyStatuses
{
    public const string Open = "open";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class DutySides
{
    public const string Assign = "assign";
    public const string Report = "report";
}

public static class DutyMediaKinds
{
    public const string Text = "text";
    public const string Image = "image";
    public const string Voice = "voice";
}
