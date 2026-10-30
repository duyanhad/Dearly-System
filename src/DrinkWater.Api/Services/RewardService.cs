using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

/// <summary>Tiền thưởng khi đạt mục tiêu ngày của từng nhu cầu (thuốc / bữa ăn / học / giấc ngủ / cân nặng).</summary>
public sealed class RewardService(AppDbContext db)
{
    /// <summary>
    /// Thưởng đúng một lần mỗi ngày cho một nhu cầu. Đơn vị tiền và số tiền lấy theo nhóm kỷ nguyên
    /// (`EraGroup.Slug` trùng mã nhu cầu); nhóm chưa gắn tiền riêng thì cộng vào "giọt" mặc định.
    /// Trả về số tiền vừa thưởng (0 = chưa đạt mục tiêu hoặc đã thưởng rồi).
    /// </summary>
    public async Task<int> AwardIfReachedAsync(Guid userId, string kind, DateOnly? date = null)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.Today);
        if (await db.NeedRewards.AnyAsync(r => r.UserId == userId && r.Kind == kind && r.Date == day))
            return 0;
        if (!await ReachedAsync(userId, kind, day)) return 0;

        var group = await db.EraGroups
            .Include(g => g.Currency)
            .FirstOrDefaultAsync(g => g.Slug == kind);
        var amount = group?.AwardBase ?? WaterCoins.BaseReward;
        if (amount <= 0) return 0;

        var currency = group?.Currency;
        if (currency is null || currency.IsDefault)
        {
            var user = await db.Users.FindAsync(userId);
            if (user is null) return 0;
            user.Coins += amount;
        }
        else
        {
            var row = await db.UserCurrencies
                .FirstOrDefaultAsync(x => x.UserId == userId && x.CurrencyId == currency.Id);
            if (row is null)
                db.UserCurrencies.Add(new UserCurrency { UserId = userId, CurrencyId = currency.Id, Amount = amount });
            else
                row.Amount += amount;
        }

        db.NeedRewards.Add(new NeedReward { UserId = userId, Kind = kind, Date = day, Amount = amount });
        db.Notifications.Add(new Notification
        {
            ReceiverId = userId,
            Title = "Nhận thưởng hôm nay!",
            Message = $"+{amount} {(currency?.Name ?? "giọt")} vì đã đạt mục tiêu {KindName(kind)}.",
            Type = NotificationTypes.Reward,
        });
        await db.SaveChangesAsync();
        return amount;
    }

    private async Task<bool> ReachedAsync(Guid userId, string kind, DateOnly day)
    {
        var target = await db.HabitSettings
            .Where(h => h.UserId == userId && h.Kind == kind)
            .Select(h => h.TargetValue)
            .FirstOrDefaultAsync();

        if (kind == "sleep")
        {
            var goal = target > 0 ? target : 480;
            var slept = await db.SleepLogs
                .Where(l => l.UserId == userId && l.Date == day && l.Status == "approved")
                .SumAsync(l => (int?)l.Minutes) ?? 0;
            return slept >= goal;
        }

        if (kind == "weight")
        {
            var goal = target > 0 ? target : 60000;
            var latest = await db.HabitLogs
                .Where(l => l.UserId == userId && l.Kind == kind && l.Date == day && l.Status == "approved" && l.Value != null)
                .OrderByDescending(l => l.TimeMinutes)
                .Select(l => l.Value!.Value)
                .FirstOrDefaultAsync();
            return latest > 0 && Math.Abs(latest - goal) <= 200;
        }

        var need = target > 0 ? target : kind switch { "study" => 2, _ => 3 };
        var done = await db.HabitLogs.CountAsync(l => l.UserId == userId && l.Kind == kind && l.Date == day && l.Status == "approved");
        return done >= need;
    }

    private static string KindName(string kind) => kind switch
    {
        "medicine" => "uống thuốc",
        "meal" => "bữa ăn",
        "study" => "học tập",
        "sleep" => "giấc ngủ",
        "weight" => "cân nặng",
        _ => kind,
    };
}
