using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

public sealed class WaterService(AppDbContext db, IClock clock)
{
    /// <summary>Phần trăm hoàn thành — làm tròn nửa lên để trang Hôm nay và trang Partner luôn khớp nhau.</summary>
    public static int Percent(int drankMl, int targetMl) =>
        targetMl <= 0 ? 0 : (int)Math.Round(drankMl * 100.0 / targetMl, MidpointRounding.AwayFromZero);

    public async Task<WaterLog> AddLogAsync(
        Guid userId, int amountMl, string? period, string? imageUrl, string? note)
    {
        var user = await db.Users.FindAsync(userId)
                   ?? throw new InvalidOperationException("User không tồn tại");

        var nowUtc = clock.UtcNow;
        var log = new WaterLog
        {
            UserId = userId,
            AmountMl = amountMl,
            Period = period ?? Periods.FromLocalTime(clock.LocalTimeNow),
            ImageUrl = imageUrl,
            Note = note,
            Status = StatusFor(user, imageUrl),
            LogTime = nowUtc
        };
        db.WaterLogs.Add(log);
        await db.SaveChangesAsync();

        await RecomputeDayAsync(user, clock.ToLocalDate(nowUtc));
        return log;
    }

    /// <summary>Có ảnh + có partner thì phải chờ partner duyệt mới được tính.</summary>
    private static string StatusFor(User user, string? imageUrl) => imageUrl switch
    {
        null => LogStatuses.Unverified,
        _ when user.PartnerId is not null => LogStatuses.Pending,
        _ => LogStatuses.Approved
    };

    /// <summary>Người dùng có bật "bắt buộc kèm ảnh" không.</summary>
    public Task<bool> RequiresPhotoAsync(Guid userId) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.RequirePhoto);

    public async Task<TodayDto> GetTodayAsync(Guid userId)
    {
        var user = await db.Users.FindAsync(userId)
                   ?? throw new InvalidOperationException("User không tồn tại");
        var today = clock.TodayLocal;

        var logs = await LogsOfDayAsync(userId, today);
        var counted = logs.Where(l => LogStatuses.Counted.Contains(l.Status)).ToList();
        // Phần bị partner trừ tách riêng để tiến độ theo ca không bị âm.
        var positive = counted.Where(l => l.AmountMl > 0).ToList();
        // Không bao giờ để tổng bị âm, kể cả khi bị trừ quá tay.
        var total = Math.Max(0, counted.Sum(l => l.AmountMl));
        var deducted = -counted.Where(l => l.AmountMl < 0).Sum(l => l.AmountMl);

        var periods = new List<PeriodProgressDto>
        {
            PeriodProgress(Periods.Morning, positive, user.MorningTargetMl),
            PeriodProgress(Periods.Afternoon, positive, user.AfternoonTargetMl),
            PeriodProgress(Periods.Evening, positive, user.EveningTargetMl)
        };

        return new TodayDto(
            today,
            total,
            user.TargetWaterMl,
            Percent(total, user.TargetWaterMl),
            total >= user.TargetWaterMl,
            await ActiveStreakAsync(userId, today),
            logs.Where(l => l.Status == LogStatuses.Pending).Sum(l => l.AmountMl),
            deducted,
            periods,
            logs.Select(ToDto).ToList());
    }

    public async Task<DayDto> GetDayAsync(Guid userId, DateOnly date)
    {
        var user = await db.Users.FindAsync(userId)
                   ?? throw new InvalidOperationException("User không tồn tại");

        var logs = await LogsOfDayAsync(userId, date);
        var total = Math.Max(0, logs.Where(l => LogStatuses.Counted.Contains(l.Status)).Sum(l => l.AmountMl));

        return new DayDto(date, total, user.TargetWaterMl, total >= user.TargetWaterMl,
            logs.Select(ToDto).ToList());
    }

    public async Task<CalendarDto> GetCalendarAsync(Guid userId, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var days = await db.DailySummaries
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Date >= first && s.Date <= last)
            .OrderBy(s => s.Date)
            .Select(s => new CalendarDayDto(s.Date, s.TotalDrankMl, s.TargetMl, s.IsCompleted, s.StreakCount))
            .ToListAsync();

        return new CalendarDto(year, month, await ActiveStreakAsync(userId, clock.TodayLocal), days);
    }

    /// <summary>Gọi lại sau khi partner duyệt/từ chối để ml và streak của ngày đó khớp lại.</summary>
    public async Task RefreshDayAsync(Guid userId, DateOnly date)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return;
        await RecomputeDayAsync(user, date);
    }

    /// <summary>
    /// Partner trừ nước trong cốc của người kia (chỉ trừ tối đa bằng lượng đang có).
    /// Ghi một log âm với status Deducted để mọi phép tính tổng tự trừ ra.
    /// </summary>
    public async Task<DeductResultDto> DeductAsync(Guid partnerId, Guid targetUserId, int amountMl)
    {
        var target = await db.Users.FindAsync(targetUserId)
                     ?? throw new InvalidOperationException("Người dùng không tồn tại");

        if (target.PartnerId != partnerId)
            throw new UnauthorizedAccessException("Bạn không phải partner của người này");

        var today = clock.TodayLocal;
        var logs = await LogsOfDayAsync(target.Id, today);
        var current = logs.Where(l => LogStatuses.Counted.Contains(l.Status)).Sum(l => l.AmountMl);

        if (current <= 0)
            throw new InvalidOperationException("Cốc của người này đang trống");

        var deducted = Math.Min(Math.Max(amountMl, 1), current);

        db.WaterLogs.Add(new WaterLog
        {
            UserId = target.Id,
            AmountMl = -deducted,
            Period = Periods.FromLocalTime(clock.LocalTimeNow),
            Note = "Partner trừ nước",
            Status = LogStatuses.Deducted,
            LogTime = clock.UtcNow
        });
        await db.SaveChangesAsync();

        await RecomputeDayAsync(target, today);

        var partner = await db.Users.AsNoTracking()
            .Where(u => u.Id == partnerId)
            .Select(u => u.FullName)
            .FirstAsync();

        var total = current - deducted;
        return new DeductResultDto(
            deducted, total, target.TargetWaterMl, Percent(total, target.TargetWaterMl), partner);
    }

    /// <summary>A day with no log at all breaks the chain, so a gap must not be skipped.</summary>
    private async Task<DailySummary?> RecomputeDayAsync(User user, DateOnly date)
    {
        var logs = await LogsOfDayAsync(user.Id, date, tracked: true);
        var total = logs.Where(l => LogStatuses.Counted.Contains(l.Status)).Sum(l => l.AmountMl);

        var summary = await db.DailySummaries
            .FirstOrDefaultAsync(s => s.UserId == user.Id && s.Date == date);

        var wasCompleted = summary?.IsCompleted ?? false;
        if (summary is null)
        {
            // Unique(UserId, Date) makes a simultaneous double insert fail; the client retries.
            // shortcut: no transaction around the insert, add one if logs ever arrive concurrently.
            summary = new DailySummary { UserId = user.Id, Date = date };
            db.DailySummaries.Add(summary);
        }

        summary.TotalDrankMl = Math.Max(0, total);
        summary.TargetMl = user.TargetWaterMl;
        summary.IsCompleted = total >= user.TargetWaterMl;
        await db.SaveChangesAsync();

        await RecomputeStreaksAsync(user.Id, date, clock.TodayLocal);

        if (!wasCompleted && summary.IsCompleted)
        {
            var latestImage = logs
                .Where(l => l.ImageUrl != null && l.Status == LogStatuses.Approved)
                .MaxBy(l => l.LogTime)?.ImageUrl;

            // Thưởng "giọt": chuỗi càng dài thưởng càng nhiều; chỉ cộng một lần cho mỗi ngày.
            var reward = 0;
            if (!summary.CoinsAwarded)
            {
                reward = WaterCoins.RewardFor(summary.StreakCount);
                user.Coins += reward;
                summary.CoinsAwarded = true;
            }

            db.Notifications.Add(new Notification
            {
                ReceiverId = user.Id,
                Title = "Đạt mục tiêu ngày!",
                Message = reward > 0
                    ? $"Bạn đã uống {total}/{user.TargetWaterMl} ml hôm nay. +{reward} giọt (chuỗi {summary.StreakCount} ngày)."
                    : $"Bạn đã uống {total}/{user.TargetWaterMl} ml hôm nay. Tiếp tục giữ chuỗi nhé!",
                ImageUrl = latestImage,
                Type = NotificationTypes.GoalAchieved
            });
            await db.SaveChangesAsync();
        }

        return summary;
    }

    private async Task RecomputeStreaksAsync(Guid userId, DateOnly fromDate, DateOnly today)
    {
        var anchorDate = fromDate.AddDays(-1);
        var anchor = await db.DailySummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Date == anchorDate);

        var rows = await db.DailySummaries
            .Where(s => s.UserId == userId && s.Date >= fromDate && s.Date <= today)
            .OrderBy(s => s.Date)
            .ToListAsync();

        var streaks = StreakCalculator.Compute(
            rows.Select(r => (r.Date, r.IsCompleted)).ToList(),
            anchor is { IsCompleted: true } ? anchorDate : null,
            anchor?.StreakCount ?? 0);

        for (var i = 0; i < rows.Count; i++)
            rows[i].StreakCount = streaks[i].Streak;

        await db.SaveChangesAsync();
    }

    /// <summary>Streak surviving today: today's chain if already done, otherwise yesterday's.</summary>
    private async Task<int> ActiveStreakAsync(Guid userId, DateOnly today)
    {
        var todayRow = await db.DailySummaries.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Date == today);
        if (todayRow is { IsCompleted: true }) return todayRow.StreakCount;

        var yesterday = await db.DailySummaries.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Date == today.AddDays(-1));
        return yesterday is { IsCompleted: true } ? yesterday.StreakCount : 0;
    }

    private Task<List<WaterLog>> LogsOfDayAsync(Guid userId, DateOnly date, bool tracked = false)
    {
        var query = tracked ? db.WaterLogs : db.WaterLogs.AsNoTracking();
        return query
            .Where(l => l.UserId == userId
                        && l.LogTime >= clock.LocalToUtc(date, TimeOnly.MinValue)
                        && l.LogTime < clock.LocalToUtc(date.AddDays(1), TimeOnly.MinValue))
            .OrderByDescending(l => l.LogTime)
            .ToListAsync();
    }

    private static PeriodProgressDto PeriodProgress(string period, List<WaterLog> logs, int targetMl)
    {
        var drank = logs.Where(l => l.Period == period).Sum(l => l.AmountMl);
        return new PeriodProgressDto(period, drank, targetMl, Percent(drank, targetMl));
    }

    private static WaterLogDto ToDto(WaterLog l) =>
        new(l.Id, l.AmountMl, l.Period, l.ImageUrl, l.Note, l.Status, l.LogTime);
}
