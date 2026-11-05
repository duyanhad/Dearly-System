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

    /// <summary>Bữa ăn: breakfast | lunch | dinner | snack | extra (rỗng với nhu cầu khác).</summary>
    public string? MealSlot { get; set; }

    /// <summary>pending | approved | rejected.</summary>
    public string Status { get; set; } = "approved";

    /// <summary>Giá trị số của lần ghi (cân nặng: gam). Null với các nhu cầu chỉ đếm số lần.</summary>
    public int? Value { get; set; }

    public string? RejectReason { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Cách tính "đã làm được bao nhiêu" của từng nhu cầu — dùng chung cho màn Hôm nay, trang partner,
/// chuỗi thành tích, lịch và phần thưởng để mọi nơi ra cùng một con số.
/// Bữa ăn / uống thuốc chỉ tính **ca chính** và mỗi ca tối đa 1 lần;
/// bữa phụ, ăn vặt, ca lạ hoặc lần ghi không chọn đúng ca thì chỉ ghi nhận, không cộng %.
/// </summary>
public static class HabitProgress
{
    /// <summary>Ba bữa chính được tính %.</summary>
    public static readonly string[] MealSlots = ["breakfast", "lunch", "dinner"];

    /// <summary>Ba ca uống thuốc được tính %.</summary>
    public static readonly string[] MedicineSlots = ["morning", "noon", "night"];

    /// <summary>Slot có được tính vào chỉ tiêu không (null = người dùng không chọn đúng ca → không tính).</summary>
    public static bool CountsForGoal(string kind, string? slot) => kind switch
    {
        "meal" => slot is not null && MealSlots.Contains(slot),
        "medicine" => slot is not null && MedicineSlots.Contains(slot),
        _ => true,
    };

    /// <summary>Số mục đã duyệt được cộng vào chỉ tiêu của một nhu cầu.</summary>
    public static int Done(string kind, IEnumerable<(string? Slot, string Status)> logs)
    {
        var approved = logs.Where(l => l.Status == "approved").ToList();
        return kind switch
        {
            "meal" => MealSlots.Count(slot => approved.Any(l => l.Slot == slot)),
            "medicine" => MedicineSlots.Count(slot => approved.Any(l => l.Slot == slot)),
            _ => approved.Count,
        };
    }
}
