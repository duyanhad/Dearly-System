using DrinkWater.Api.Entities;

namespace DrinkWater.Api.Services;

/// <summary>
/// Quyết định "có nhắc uống nước lúc này không" + câu thông báo theo tính cách.
/// Tách khỏi background service để test được mà không cần DB/đồng hồ thật.
/// </summary>
public static class ReminderCadence
{
    /// <summary>Chỉ nhắc trong khung giờ người dùng chọn (giờ địa phương), tránh đánh thức lúc 3 giờ sáng.</summary>
    public const int DefaultStartHour = 7;
    public const int DefaultEndHour = 23;

    /// <summary>Khoảng cách nhắc lại khi người dùng vẫn chưa uống, theo tính cách.</summary>
    public static TimeSpan RepeatGap(string style) => style switch
    {
        // Ác quỷ: nhắc liên tục (mỗi phút) cho tới khi uống.
        ReminderStyles.Demon => TimeSpan.FromMinutes(1),
        ReminderStyles.Gentle => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(3)
    };

    public static bool InWindow(DateTime localNow, int startHour = DefaultStartHour, int endHour = DefaultEndHour) =>
        localNow.Hour >= startHour && localNow.Hour < endHour;

    /// <summary>
    /// <paramref name="lastDrinkLocal"/> = lần uống nước gần nhất (null = hôm nay chưa uống),
    /// <paramref name="lastReminderLocal"/> = lần nhắc gần nhất (null = chưa nhắc).
    /// Nhắc khi đã quá <paramref name="intervalMinutes"/> kể từ lần uống gần nhất (hoặc từ đầu ngày),
    /// và đã quá khoảng cách nhắc lại của tính cách đó.
    /// </summary>
    public static bool ShouldRemind(
        DateTime localNow,
        DateTime? lastDrinkLocal,
        DateTime? lastReminderLocal,
        int intervalMinutes,
        string style,
        int startHour = DefaultStartHour,
        int endHour = DefaultEndHour)
    {
        if (!InWindow(localNow, startHour, endHour)) return false;
        if (intervalMinutes < 15) return false;

        // Chưa uống lần nào → cứ nhắc; đã uống thì phải quá khoảng cách mới nhắc.
        if (lastDrinkLocal is { } drank && localNow - drank < TimeSpan.FromMinutes(intervalMinutes))
            return false;

        if (lastReminderLocal is { } reminded && localNow - reminded < RepeatGap(style)) return false;

        return true;
    }

    /// <summary>Câu thông báo theo tính cách; càng lâu không uống càng gắt (chỉ ở mức Ác quỷ).</summary>
    public static (string Title, string Message) Message(string style, int ml, int minutesSinceDrink)
    {
        var hours = Math.Max(1, minutesSinceDrink / 60);
        return style switch
        {
            ReminderStyles.Gentle => (
                "Nhắc nhẹ thôi 💧",
                $"Bạn uống {ml} ml nước nhé — cơ thể đang chờ đấy."),
            ReminderStyles.Demon when minutesSinceDrink >= 120 => (
                "😈 UỐNG NGAY!",
                $"{hours} tiếng rồi không có giọt nào! {ml} ml, ngay bây giờ!"),
            ReminderStyles.Demon => (
                "😈 Đến giờ rồi!",
                $"Uống {ml} ml đi! Đừng để ta phải nhắc lần nữa."),
            _ => (
                "Đến giờ uống nước 💧",
                $"Bạn nên uống {ml} ml nước bây giờ.")
        };
    }
}
