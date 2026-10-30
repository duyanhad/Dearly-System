using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Tests.Integration;

[Collection(nameof(PostgresCollection))]
public class ProofApprovalTests(PostgresFixture fixture)
{
    private readonly FakeClock _clock = new();

    private async Task<(Guid UserId, Guid? PartnerId)> CreateUserAsync(bool withPartner)
    {
        await using var db = fixture.CreateContext();

        Guid? partnerId = null;
        if (withPartner)
        {
            var partner = new User
            {
                Email = $"partner{Guid.NewGuid():N}@test.local",
                FullName = "Partner",
                PasswordHash = "x"
            };
            db.Users.Add(partner);
            await db.SaveChangesAsync();
            partnerId = partner.Id;
        }

        var user = new User
        {
            Email = $"owner{Guid.NewGuid():N}@test.local",
            FullName = "Owner",
            PasswordHash = "x",
            TargetWaterMl = 1000,
            MorningTargetMl = 1000,
            PartnerId = partnerId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (user.Id, partnerId);
    }

    private async Task<TodayDtoSnapshot> TodayAsync(Guid userId)
    {
        await using var db = fixture.CreateContext();
        var today = await new WaterService(db, _clock).GetTodayAsync(userId);
        return new TodayDtoSnapshot(today.TotalDrankMl, today.PendingMl, today.Logs.Select(l => l.Status).ToList());
    }

    private sealed record TodayDtoSnapshot(int Total, int Pending, List<string> Statuses);

    [Fact]
    public async Task Photo_with_partner_waits_for_approval_and_is_not_counted()
    {
        var (userId, _) = await CreateUserAsync(withPartner: true);

        await using (var db = fixture.CreateContext())
            await new WaterService(db, _clock)
                .AddLogAsync(userId, 500, Periods.Morning, "/uploads/a.jpg", "ảnh");

        var today = await TodayAsync(userId);
        Assert.Equal(0, today.Total);
        Assert.Equal(500, today.Pending);
        Assert.Equal([LogStatuses.Pending], today.Statuses);
    }

    [Fact]
    public async Task Photo_without_partner_is_approved_immediately()
    {
        var (userId, _) = await CreateUserAsync(withPartner: false);

        await using (var db = fixture.CreateContext())
            await new WaterService(db, _clock)
                .AddLogAsync(userId, 500, Periods.Morning, "/uploads/a.jpg", null);

        var today = await TodayAsync(userId);
        Assert.Equal(500, today.Total);
        Assert.Equal(0, today.Pending);
        Assert.Equal([LogStatuses.Approved], today.Statuses);
    }

    [Fact]
    public async Task Log_without_photo_is_unverified_and_still_counted()
    {
        var (userId, _) = await CreateUserAsync(withPartner: true);

        await using (var db = fixture.CreateContext())
            await new WaterService(db, _clock).AddLogAsync(userId, 300, Periods.Morning, null, null);

        var today = await TodayAsync(userId);
        Assert.Equal(300, today.Total);
        Assert.Equal(0, today.Pending);
        Assert.Equal([LogStatuses.Unverified], today.Statuses);
    }

    [Fact]
    public async Task Approving_later_adds_the_ml_to_the_day()
    {
        var (userId, partnerId) = await CreateUserAsync(withPartner: true);

        int logId;
        await using (var db = fixture.CreateContext())
            logId = (await new WaterService(db, _clock)
                .AddLogAsync(userId, 400, Periods.Morning, "/uploads/a.jpg", null)).Id;

        Assert.Equal(0, (await TodayAsync(userId)).Total);

        await using (var db = fixture.CreateContext())
        {
            var log = await db.WaterLogs.SingleAsync(l => l.Id == logId);
            log.Status = LogStatuses.Approved;
            log.ReviewedBy = partnerId;
            log.ReviewedAt = _clock.UtcNow;
            await db.SaveChangesAsync();

            var service = new WaterService(db, _clock);
            await service.RefreshDayAsync(log.UserId, _clock.ToLocalDate(log.LogTime));
        }

        var today = await TodayAsync(userId);
        Assert.Equal(400, today.Total);
        Assert.Equal(0, today.Pending);
    }
}
