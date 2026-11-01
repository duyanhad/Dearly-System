using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

public sealed class SocialService(AppDbContext db, IClock clock)
{
    public async Task<List<FeedItemDto>> FeedAsync(Guid me)
    {
        var ids = await db.Follows
            .Where(f => f.FollowerId == me && f.Status == FollowStatuses.Accepted)
            .Select(f => f.FollowingId)
            .ToListAsync();

        if (ids.Count == 0) return [];

        var nicknames = await db.FriendNicknames
            .AsNoTracking()
            .Where(n => n.OwnerId == me)
            .ToDictionaryAsync(n => n.TargetId, n => n.Nickname);
        var today = clock.TodayLocal;
        var from = clock.LocalToUtc(today, TimeOnly.MinValue);
        var to = clock.LocalToUtc(today.AddDays(1), TimeOnly.MinValue);

        var users = await db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.Email, u.TargetWaterMl, u.AvatarUrl })
            .ToListAsync();

        var logs = await db.WaterLogs
            .AsNoTracking()
            .Where(l => ids.Contains(l.UserId) && l.LogTime >= from && l.LogTime < to)
            .OrderByDescending(l => l.LogTime)
            .ToListAsync();

        return users.Select(u =>
        {
            var mine = logs.Where(l => l.UserId == u.Id).ToList();
            var counted = mine.Where(l => LogStatuses.Counted.Contains(l.Status)).ToList();
            var total = counted.Sum(l => l.AmountMl);

            // Ảnh chưa được partner duyệt thì chưa hiện trên bảng tin.
            var latestPhoto = mine.FirstOrDefault(l => l.ImageUrl != null && l.Status == LogStatuses.Approved);
            var nickname = nicknames.GetValueOrDefault(u.Id);

            return new FeedItemDto(
                u.Id, u.FullName, nickname, u.AvatarUrl, u.Email,
                total, u.TargetWaterMl,
                WaterService.Percent(total, u.TargetWaterMl),
                total >= u.TargetWaterMl,
                latestPhoto?.ImageUrl,
                latestPhoto?.Note,
                latestPhoto?.LogTime);
        }).ToList();
    }

    /// <summary>Bạn bè = những người tôi theo dõi (đã chấp nhận) hoặc đang theo dõi tôi.</summary>
    public async Task<List<FriendDto>> FriendsAsync(Guid me)
    {
        var rows = await db.Follows
            .AsNoTracking()
            .Where(f => f.Status == FollowStatuses.Accepted && (f.FollowerId == me || f.FollowingId == me))
            .ToListAsync();

        var others = rows
            .Select(f => f.FollowerId == me ? f.FollowingId : f.FollowerId)
            .Distinct()
            .ToList();

        if (others.Count == 0) return [];

        // Partner tôi đã chọn (mỗi tài khoản chỉ có 1) + người tôi đang xin duyệt ảnh.
        var myIds = await db.Users.AsNoTracking()
            .Where(u => u.Id == me)
            .Select(u => new { u.PartnerId, u.PendingPartnerId })
            .FirstOrDefaultAsync();
        var myPartnerId = myIds?.PartnerId;
        var myPendingId = myIds?.PendingPartnerId;

        var users = await db.Users
            .AsNoTracking()
            .Where(u => others.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.FullName, u.AvatarUrl, u.PartnerId })
            .ToListAsync();

        var myNicknames = await db.FriendNicknames.AsNoTracking()
            .Where(n => n.OwnerId == me && others.Contains(n.TargetId))
            .ToDictionaryAsync(n => n.TargetId, n => n.Nickname);

        var theirNicknames = await db.FriendNicknames.AsNoTracking()
            .Where(n => n.TargetId == me && others.Contains(n.OwnerId))
            .ToDictionaryAsync(n => n.OwnerId, n => n.Nickname);

        return users.Select(u =>
        {
            var mine = rows.FirstOrDefault(f => f.FollowerId == me && f.FollowingId == u.Id);
            var theirs = rows.FirstOrDefault(f => f.FollowerId == u.Id && f.FollowingId == me);

            return new FriendDto(
                u.Id, u.Email, u.FullName,
                myNicknames.GetValueOrDefault(u.Id),
                theirNicknames.GetValueOrDefault(u.Id),
                u.AvatarUrl,
                mine is not null,
                theirs is not null,
                myPartnerId == u.Id,
                myPendingId == u.Id);
        })
        .OrderBy(f => f.Nickname ?? f.FullName)
        .ToList();
    }

    /// <summary>Đặt biệt hiệu cho một người bạn (đã follow hoặc đang follow mình).</summary>
    public async Task<bool> SetNicknameAsync(Guid me, Guid targetId, string? nickname)
    {
        var isFriend = await db.Follows.AnyAsync(f =>
            f.Status == FollowStatuses.Accepted &&
            ((f.FollowerId == me && f.FollowingId == targetId) ||
             (f.FollowerId == targetId && f.FollowingId == me)));

        // Partner cũng đặt được biệt hiệu cho người mình duyệt ảnh.
        var isMyClient = await db.Users.AnyAsync(u => u.Id == targetId && u.PartnerId == me);

        if (!isFriend && !isMyClient) return false;

        var row = await db.FriendNicknames
            .FirstOrDefaultAsync(n => n.OwnerId == me && n.TargetId == targetId);

        var trimmed = nickname?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            if (row is not null) db.FriendNicknames.Remove(row);
        }
        else if (row is null)
        {
            db.FriendNicknames.Add(new FriendNickname { OwnerId = me, TargetId = targetId, Nickname = trimmed });
        }
        else
        {
            row.Nickname = trimmed;
        }

        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Người tôi đã xin làm người duyệt ảnh (đang chờ họ đồng ý). Null nếu không có.</summary>
    public async Task<PartnerRequestDto?> PendingPartnerAsync(Guid me)
    {
        var pendingId = await db.Users.AsNoTracking()
            .Where(u => u.Id == me)
            .Select(u => u.PendingPartnerId)
            .FirstOrDefaultAsync();

        if (pendingId is null) return null;

        return await db.Users.AsNoTracking()
            .Where(u => u.Id == pendingId)
            .Select(u => new PartnerRequestDto(
                u.Id, u.FullName, u.AvatarUrl,
                db.FriendNicknames.Where(n => n.OwnerId == me && n.TargetId == u.Id)
                    .Select(n => n.Nickname).FirstOrDefault()))
            .FirstOrDefaultAsync();
    }

    /// <summary>Những người đang xin tôi duyệt ảnh minh chứng (chờ tôi đồng ý).</summary>
    public Task<List<PartnerRequestDto>> PartnerRequestsAsync(Guid me) =>
        db.Users
            .AsNoTracking()
            .Where(u => u.PendingPartnerId == me)
            .OrderBy(u => u.FullName)
            .Select(u => new PartnerRequestDto(
                u.Id, u.FullName, u.AvatarUrl,
                db.FriendNicknames.Where(n => n.OwnerId == me && n.TargetId == u.Id)
                    .Select(n => n.Nickname).FirstOrDefault()))
            .ToListAsync();

    public Task<List<UserSummaryDto>> FollowersAsync(Guid me) =>
        MapUsersAsync(
            db.Follows.Where(f => f.FollowingId == me && f.Status == FollowStatuses.Accepted)
                .Select(f => f.FollowerId),
            me, direction: "incoming");

    public Task<List<UserSummaryDto>> FollowingAsync(Guid me) =>
        MapUsersAsync(db.Follows.Where(f => f.FollowerId == me).Select(f => f.FollowingId),
            me, direction: "outgoing");

    public Task<List<UserSummaryDto>> FollowRequestsAsync(Guid me) =>
        MapUsersAsync(
            db.Follows.Where(f => f.FollowingId == me && f.Status == FollowStatuses.Pending)
                .Select(f => f.FollowerId),
            me, direction: "incoming");

    /// <summary>Tất cả phiếu chờ tôi duyệt: nước + giấc ngủ + thuốc/bữa ăn/học tập/cân nặng.</summary>
    public async Task<List<PendingProofDto>> PendingProofsAsync(Guid partnerId)
    {
        var water = await db.WaterLogs
            .AsNoTracking()
            .Where(l => l.Status == LogStatuses.Pending && l.User.PartnerId == partnerId)
            .Select(l => new PendingProofDto(
                l.Id.ToString(), l.UserId, l.User.FullName, l.User.AvatarUrl, l.AmountMl, l.Period,
                l.ImageUrl, l.Note, l.LogTime, "water", null, null, null))
            .ToListAsync();

        var habits = await (
            from l in db.HabitLogs.AsNoTracking()
            join u in db.Users.AsNoTracking() on l.UserId equals u.Id
            where l.Status == "pending" && u.PartnerId == partnerId
            select new PendingProofDto(
                l.Id.ToString(), l.UserId, u.FullName, u.AvatarUrl, 0, "all",
                l.PhotoUrl, l.Note, l.CreatedAt, l.Kind, l.Value, null, null)
        ).ToListAsync();

        var sleeps = await (
            from l in db.SleepLogs.AsNoTracking()
            join u in db.Users.AsNoTracking() on l.UserId equals u.Id
            where l.Status == "pending" && u.PartnerId == partnerId
            select new PendingProofDto(
                l.Id.ToString(), l.UserId, u.FullName, u.AvatarUrl, 0, "all",
                l.PhotoUrl, l.Note, l.CreatedAt, "sleep", l.Minutes,
                (l.BedMinutes / 60).ToString("00") + ":" + (l.BedMinutes % 60).ToString("00"),
                (l.WakeMinutes / 60).ToString("00") + ":" + (l.WakeMinutes % 60).ToString("00"))
        ).ToListAsync();

        return water.Concat(habits).Concat(sleeps).OrderBy(l => l.LogTime).ToList();
    }

    public static bool IsValidFollow(Guid me, Guid target) => me != target && target != Guid.Empty;

    /// <summary>Những người đã chọn tôi làm partner duyệt ảnh — kèm tiến độ hôm nay của họ.</summary>
    public async Task<List<PartnerClientDto>> PartnerClientsAsync(Guid partnerId)
    {
        var today = clock.TodayLocal;
        var clients = await db.Users
            .AsNoTracking()
            .Where(u => u.PartnerId == partnerId)
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                u.AvatarUrl,
                u.TargetWaterMl,
                Cup = db.CupPresets
                    .Where(c => c.UserId == u.Id && c.IsDefault)
                    .Select(c => c.ImageUrl)
                    .FirstOrDefault()
                    ?? db.CupPresets
                        .Where(c => c.UserId == u.Id && c.ImageUrl != null)
                        .OrderBy(c => c.Id)
                        .Select(c => c.ImageUrl)
                        .FirstOrDefault(),
                CupCutout = db.CupPresets
                    .Where(c => c.UserId == u.Id && c.IsDefault)
                    .Select(c => c.CutoutUrl)
                    .FirstOrDefault()
                    ?? db.CupPresets
                        .Where(c => c.UserId == u.Id && c.CutoutUrl != null)
                        .OrderBy(c => c.Id)
                        .Select(c => c.CutoutUrl)
                        .FirstOrDefault(),
                Today = db.DailySummaries
                    .Where(d => d.UserId == u.Id && d.Date == today)
                    .Select(d => (int?)d.TotalDrankMl)
                    .FirstOrDefault() ?? 0
            })
            .ToListAsync();

        var nicknames = await db.FriendNicknames
            .AsNoTracking()
            .Where(n => n.OwnerId == partnerId)
            .ToDictionaryAsync(n => n.TargetId, n => n.Nickname);

        return clients
            .Select(c => new PartnerClientDto(
                c.Id, c.FullName, c.Email, c.AvatarUrl, c.Today, c.TargetWaterMl, c.Cup,
                nicknames.GetValueOrDefault(c.Id), c.CupCutout))
            .ToList();
    }

    /// <summary>Người đang duyệt ảnh cho tôi (nếu đã chọn partner).</summary>
    public async Task<MyPartnerDto?> MyPartnerAsync(Guid me)
    {
        var today = clock.TodayLocal;
        var partnerId = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == me)
            .Select(u => u.PartnerId)
            .FirstOrDefaultAsync();

        if (partnerId is null) return null;

        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == partnerId)
            .Select(u => new MyPartnerDto(
                u.Id,
                u.FullName,
                u.AvatarUrl,
                db.DailySummaries
                    .Where(d => d.UserId == u.Id && d.Date == today)
                    .Select(d => (int?)d.TotalDrankMl)
                    .FirstOrDefault() ?? 0,
                u.TargetWaterMl,
                db.CupPresets
                    .Where(c => c.UserId == u.Id && c.IsDefault)
                    .Select(c => c.ImageUrl)
                    .FirstOrDefault()
                    ?? db.CupPresets
                        .Where(c => c.UserId == u.Id && c.ImageUrl != null)
                        .OrderBy(c => c.Id)
                        .Select(c => c.ImageUrl)
                        .FirstOrDefault()))
            .FirstOrDefaultAsync();
    }

    /// <summary>`direction` = "outgoing" khi trạng thái là của tôi → người kia, "incoming" là ngược lại.</summary>
    private Task<List<UserSummaryDto>> MapUsersAsync(
        IQueryable<Guid> ids, Guid me, string direction) =>
        db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .OrderBy(u => u.FullName)
            .Select(u => new UserSummaryDto(
                u.Id, u.Email, u.FullName,
                direction == "outgoing"
                    ? db.Follows.Any(f => f.FollowerId == me && f.FollowingId == u.Id)
                    : db.Follows.Any(f => f.FollowerId == u.Id && f.FollowingId == me),
                direction == "outgoing"
                    ? db.Follows
                        .Where(f => f.FollowerId == me && f.FollowingId == u.Id)
                        .Select(f => f.Status).FirstOrDefault() ?? "None"
                    : db.Follows
                        .Where(f => f.FollowerId == u.Id && f.FollowingId == me)
                        .Select(f => f.Status).FirstOrDefault() ?? "None",
                u.PartnerId == me,
                direction == "outgoing"
                    ? db.FriendNicknames
                        .Where(n => n.OwnerId == me && n.TargetId == u.Id)
                        .Select(n => n.Nickname).FirstOrDefault()
                    : db.FriendNicknames
                        .Where(n => n.OwnerId == u.Id && n.TargetId == me)
                        .Select(n => n.Nickname).FirstOrDefault(),
                u.AvatarUrl))
            .ToListAsync();
}

