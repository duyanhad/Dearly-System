using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Tests.Integration;

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

[Collection(nameof(PostgresCollection))]
public class WaterServiceTests(PostgresFixture fixture)
{
    private readonly FakeClock _clock = new();

    private async Task<Guid> CreateUserAsync(int target = 1000)
    {
        await using var db = fixture.CreateContext();
        var user = new User
        {
            Email = $"u{Guid.NewGuid():N}@test.local",
            FullName = "Test",
            PasswordHash = "x",
            TargetWaterMl = target,
            MorningTargetMl = target / 2,
            AfternoonTargetMl = target / 2,
            EveningTargetMl = 0
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task Adding_water_creates_the_daily_summary()
    {
        var userId = await CreateUserAsync();

        await using (var db = fixture.CreateContext())
        {
            var service = new WaterService(db, _clock);
            await service.AddLogAsync(userId, 400, Periods.Morning, null, "ca sang");
        }

        await using var check = fixture.CreateContext();
        var summary = await check.DailySummaries.SingleAsync(s => s.UserId == userId);
        Assert.Equal(400, summary.TotalDrankMl);
        Assert.False(summary.IsCompleted);
        Assert.Equal(new DateOnly(2026, 1, 10), summary.Date);
    }

    [Fact]
    public async Task Reaching_the_target_completes_the_day_and_notifies_once()
    {
        var userId = await CreateUserAsync(target: 1000);

        await using (var db = fixture.CreateContext())
        {
            var service = new WaterService(db, _clock);
            await service.AddLogAsync(userId, 600, Periods.Morning, null, null);
            await service.AddLogAsync(userId, 400, Periods.Morning, null, null);
            await service.AddLogAsync(userId, 100, Periods.Afternoon, null, null);
        }

        await using var check = fixture.CreateContext();
        var summary = await check.DailySummaries.SingleAsync(s => s.UserId == userId);
        Assert.Equal(1100, summary.TotalDrankMl);
        Assert.True(summary.IsCompleted);

        var goals = await check.Notifications
            .Where(n => n.ReceiverId == userId && n.Type == NotificationTypes.GoalAchieved)
            .ToListAsync();
        Assert.Single(goals);
    }

    [Fact]
    public async Task Today_reports_period_breakdown_and_percent()
    {
        var userId = await CreateUserAsync(target: 1000);

        await using (var db = fixture.CreateContext())
        {
            var service = new WaterService(db, _clock);
            await service.AddLogAsync(userId, 300, Periods.Morning, null, null);
            await service.AddLogAsync(userId, 200, Periods.Afternoon, null, "co anh");
        }

        await using var check = fixture.CreateContext();
        var today = await new WaterService(check, _clock).GetTodayAsync(userId);

        Assert.Equal(500, today.TotalDrankMl);
        Assert.Equal(50, today.Percent);
        Assert.Equal(300, today.Periods.Single(p => p.Period == Periods.Morning).DrankMl);
        Assert.Equal(200, today.Periods.Single(p => p.Period == Periods.Afternoon).DrankMl);
        Assert.Equal(2, today.Logs.Count);
    }

    [Fact]
    public async Task Streak_counts_consecutive_completed_days()
    {
        var userId = await CreateUserAsync(target: 1000);
        await SeedCompletedDayAsync(userId, new DateOnly(2026, 1, 8), streak: 1);
        await SeedCompletedDayAsync(userId, new DateOnly(2026, 1, 9), streak: 2);

        await using (var db = fixture.CreateContext())
            await new WaterService(db, _clock).AddLogAsync(userId, 1000, Periods.Morning, null, null);

        await using var check = fixture.CreateContext();
        var today = await new WaterService(check, _clock).GetTodayAsync(userId);
        Assert.Equal(3, today.StreakCount);

        var calendar = await new WaterService(check, _clock).GetCalendarAsync(userId, 2026, 1);
        Assert.Equal(3, calendar.CurrentStreak);
    }

    [Fact]
    public async Task A_missing_day_breaks_the_streak()    {
        var userId = await CreateUserAsync(target: 1000);
        await SeedCompletedDayAsync(userId, new DateOnly(2026, 1, 8));

        await using (var db = fixture.CreateContext())
            await new WaterService(db, _clock).AddLogAsync(userId, 1000, Periods.Morning, null, null);

        await using var check = fixture.CreateContext();
        var today = await new WaterService(check, _clock).GetTodayAsync(userId);
        Assert.Equal(1, today.StreakCount);
    }

    [Fact]
    public async Task Calendar_only_lists_days_that_have_data()
    {
        var userId = await CreateUserAsync(target: 1000);
        await SeedCompletedDayAsync(userId, new DateOnly(2026, 1, 8));

        await using var db = fixture.CreateContext();
        var calendar = await new WaterService(db, _clock).GetCalendarAsync(userId, 2026, 1);

        Assert.Single(calendar.Days);
        Assert.Equal(new DateOnly(2026, 1, 8), calendar.Days[0].Date);
    }

    [Fact]
    public async Task Require_photo_setting_is_on_by_default_and_can_be_turned_off()
    {
        var userId = await CreateUserAsync();

        await using (var db = fixture.CreateContext())
            Assert.True(await new WaterService(db, _clock).RequiresPhotoAsync(userId));

        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.RequirePhoto = false;
            await db.SaveChangesAsync();
        }

        await using var check = fixture.CreateContext();
        Assert.False(await new WaterService(check, _clock).RequiresPhotoAsync(userId));
    }

    private async Task SeedCompletedDayAsync(Guid userId, DateOnly date, int streak = 1)
    {
        await using var db = fixture.CreateContext();
        var start = _clock.LocalToUtc(date, TimeOnly.MinValue);
        db.WaterLogs.Add(new WaterLog
        {
            UserId = userId,
            AmountMl = 1000,
            Period = Periods.Morning,
            LogTime = start.AddHours(3)
        });
        db.DailySummaries.Add(new DailySummary
        {
            UserId = userId,
            Date = date,
            TotalDrankMl = 1000,
            TargetMl = 1000,
            IsCompleted = true,
            StreakCount = streak
        });
        await db.SaveChangesAsync();
    }
}
