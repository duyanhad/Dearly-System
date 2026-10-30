namespace DrinkWater.Api.Entities;

/// <summary>
/// Một lần đạt mục tiêu của một nhu cầu trong ngày (thuốc / bữa ăn / học tập / giấc ngủ / cân nặng).
/// Mỗi nhu cầu chỉ thưởng một lần mỗi ngày — dùng để chặn cộng tiền trùng.
/// </summary>
public class NeedReward
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>medicine | meal | study | sleep | weight</summary>
    public string Kind { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    /// <summary>Số tiền đã thưởng cho lần đạt mục tiêu này.</summary>
    public int Amount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
