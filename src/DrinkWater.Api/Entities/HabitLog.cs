namespace DrinkWater.Api.Entities;

/// <summary>Một lần ghi nhận của nhu cầu không phải nước/ngủ (uống thuốc, bữa ăn, học).</summary>
public class HabitLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>medicine | meal | study.</summary>
    public string Kind { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    /// <summary>Phút tính từ 00:00 của lúc ghi nhận.</summary>
    public int TimeMinutes { get; set; }

    public string Note { get; set; } = string.Empty;

    public string? PhotoUrl { get; set; }

    /// <summary>pending | approved | rejected.</summary>
    public string Status { get; set; } = "approved";

    /// <summary>Giá trị số của lần ghi (cân nặng: gam). Null với các nhu cầu chỉ đếm số lần.</summary>
    public int? Value { get; set; }

    public string? RejectReason { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
