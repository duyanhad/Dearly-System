using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/social")]
public class SocialController(
    AppDbContext db,
    SocialService social,
    WaterService water,
    IClock clock,
    PresenceTracker presence,
    IPushSender push,
    IWebPushSender webPush,
    IHubContext<NotificationHub> hub) : ControllerBase
{
    [HttpPost("follow/{userId:guid}")]
    public async Task<ActionResult<object>> Follow(Guid userId)
    {
        var me = User.GetUserId();
        if (!SocialService.IsValidFollow(me, userId))
            return Problem(statusCode: 400, title: "Không thể tự follow chính mình");

        if (!await db.Users.AnyAsync(u => u.Id == userId))
            return NotFound();

        var existing = await db.Follows.FirstOrDefaultAsync(f => f.FollowerId == me && f.FollowingId == userId);
        if (existing is not null)
            return new { status = existing.Status };

        // Người kia đã theo dõi tôi rồi thì coi như đồng ý luôn.
        var reverseAccepted = await db.Follows.AnyAsync(f =>
            f.FollowerId == userId && f.FollowingId == me && f.Status == FollowStatuses.Accepted);

        var follow = new Follow
        {
            FollowerId = me,
            FollowingId = userId,
            Status = reverseAccepted ? FollowStatuses.Accepted : FollowStatuses.Pending
        };
        db.Follows.Add(follow);

        // Báo cho người kia biết có lời mời kết bạn (chuông thông báo).
        if (!reverseAccepted)
        {
            var myName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
            db.Notifications.Add(new Notification
            {
                SenderId = me,
                ReceiverId = userId,
                Title = "Lời mời kết bạn",
                Message = $"{myName} muốn kết bạn với bạn.",
                Type = NotificationTypes.Friend
            });
        }

        await db.SaveChangesAsync();

        // Đẩy realtime để chuông của người nhận cập nhật ngay.
        if (!reverseAccepted)
        {
            var myName2 = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
            await hub.Clients.User(userId.ToString()).SendAsync("ReceiveNudge", new
            {
                Title = "Lời mời kết bạn",
                Message = $"{myName2} muốn kết bạn với bạn."
            });
        }

        return new { status = follow.Status };
    }

    /// <summary>
    /// Huỷ kết bạn: xoá CẢ HAI chiều để quyền riêng tư có hiệu lực ngay
    /// (nếu chỉ xoá chiều của mình, chiều còn lại vẫn làm hai người thành "bạn bè").
    /// </summary>
    [HttpDelete("unfollow/{userId:guid}")]
    public async Task<IActionResult> Unfollow(Guid userId)
    {
        var me = User.GetUserId();
        var rows = await db.Follows
            .Where(f => (f.FollowerId == me && f.FollowingId == userId) ||
                        (f.FollowerId == userId && f.FollowingId == me))
            .ToListAsync();

        if (rows.Count == 0) return NoContent();

        db.Follows.RemoveRange(rows);

        // Xoá bạn bè thì phải bỏ luôn quan hệ partner/duyệt ảnh giữa hai người,
        // nếu không người đã xoá vẫn nằm trong trang Partner của nhau.
        var pair = await db.Users.Where(u => u.Id == me || u.Id == userId).ToListAsync();
        foreach (var u in pair)
        {
            var other = u.Id == me ? userId : me;
            if (u.PartnerId == other) u.PartnerId = null;
            if (u.PendingPartnerId == other) u.PendingPartnerId = null;
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("feed")]
    public Task<List<FeedItemDto>> Feed() => social.FeedAsync(User.GetUserId());

    [HttpGet("friends")]
    public Task<List<FriendDto>> Friends() => social.FriendsAsync(User.GetUserId());

    [HttpPut("nickname/{userId:guid}")]
    public async Task<ActionResult<FriendDto>> SetNickname(Guid userId, SetNicknameRequest req)
    {
        var updated = await social.SetNicknameAsync(User.GetUserId(), userId, req.Nickname);
        if (!updated) return Problem(statusCode: 400, title: "Bạn chưa theo dõi người này");

        // Người mình duyệt ảnh có thể không nằm trong danh sách bạn bè → trả 204.
        var friends = await social.FriendsAsync(User.GetUserId());
        var friend = friends.FirstOrDefault(f => f.Id == userId);
        return friend is null ? NoContent() : friend;
    }

    [HttpGet("followers")]
    public Task<List<UserSummaryDto>> Followers() => social.FollowersAsync(User.GetUserId());

    [HttpGet("following")]
    public Task<List<UserSummaryDto>> Following() => social.FollowingAsync(User.GetUserId());

    [HttpGet("follow-requests")]
    public Task<List<UserSummaryDto>> FollowRequests() => social.FollowRequestsAsync(User.GetUserId());

    [HttpPost("follow-requests/{userId:guid}/accept")]
    public async Task<IActionResult> AcceptRequest(Guid userId)
    {
        var me = User.GetUserId();
        var request = await db.Follows.FirstOrDefaultAsync(f =>
            f.FollowerId == userId && f.FollowingId == me && f.Status == FollowStatuses.Pending);

        if (request is null) return NotFound();

        request.Status = FollowStatuses.Accepted;

        // Kết bạn là hai chiều: tạo luôn lượt theo dõi ngược để cả hai thấy tiến trình của nhau.
        var reverse = await db.Follows.FirstOrDefaultAsync(f => f.FollowerId == me && f.FollowingId == userId);
        if (reverse is null)
        {
            db.Follows.Add(new Follow
            {
                FollowerId = me,
                FollowingId = userId,
                Status = FollowStatuses.Accepted
            });
        }
        else
        {
            reverse.Status = FollowStatuses.Accepted;
        }

        var myName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
        db.Notifications.Add(new Notification
        {
            SenderId = me,
            ReceiverId = userId,
            Title = "Đã chấp nhận kết bạn",
            Message = $"{myName} và bạn giờ đã là bạn bè.",
            Type = NotificationTypes.Friend
        });

        await db.SaveChangesAsync();

        await hub.Clients.User(userId.ToString()).SendAsync("ReceiveNudge", new
        {
            Title = "Đã chấp nhận kết bạn",
            Message = $"{myName} và bạn giờ đã là bạn bè."
        });

        return NoContent();
    }

    [HttpPost("follow-requests/{userId:guid}/reject")]
    public async Task<IActionResult> RejectRequest(Guid userId)
    {
        var me = User.GetUserId();
        var request = await db.Follows.FirstOrDefaultAsync(f =>
            f.FollowerId == userId && f.FollowingId == me && f.Status == FollowStatuses.Pending);

        if (request is null) return NotFound();

        db.Follows.Remove(request);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Ảnh minh chứng của những người chọn tôi làm partner và đang chờ duyệt.</summary>
    [HttpGet("verify/pending")]
    public Task<List<PendingProofDto>> PendingProofs() => social.PendingProofsAsync(User.GetUserId());

    [HttpGet("partner/clients")]
    public Task<List<PartnerClientDto>> PartnerClients() => social.PartnerClientsAsync(User.GetUserId());

    /// <summary>Người duyệt ảnh của tôi (partner) kèm tiến độ hôm nay — dùng cho menu tròn.</summary>
    [HttpGet("partner/me")]
    public Task<MyPartnerDto?> MyPartner() => social.MyPartnerAsync(User.GetUserId());

    /// <summary>Người tôi đã xin duyệt ảnh, đang chờ họ đồng ý (null nếu không có).</summary>
    [HttpGet("partner/pending")]
    public Task<PartnerRequestDto?> PendingPartner() => social.PendingPartnerAsync(User.GetUserId());

    /// <summary>Những người đang xin tôi duyệt ảnh minh chứng.</summary>
    [HttpGet("partner/requests")]
    public Task<List<PartnerRequestDto>> PartnerRequests() => social.PartnerRequestsAsync(User.GetUserId());

    /// <summary>Đồng ý làm người duyệt ảnh cho người đã xin tôi.</summary>
    [HttpPost("partner/requests/{userId:guid}/accept")]
    public async Task<IActionResult> AcceptPartnerRequest(Guid userId)
    {
        var me = User.GetUserId();
        var requester = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.PendingPartnerId == me);
        if (requester is null) return NotFound();

        requester.PendingPartnerId = null;
        requester.PartnerId = me;

        var myName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
        db.Notifications.Add(new Notification
        {
            SenderId = me,
            ReceiverId = userId,
            Title = "Đã nhận làm partner",
            Message = $"{myName} đồng ý làm partner của bạn.",
            Type = NotificationTypes.Partner,
        });
        await db.SaveChangesAsync();

        await hub.Clients.User(userId.ToString()).SendAsync("ReceiveNudge", new
        {
            Title = "Đã nhận làm partner",
            Message = $"{myName} đồng ý làm partner của bạn.",
        });
        return NoContent();
    }

    /// <summary>Từ chối làm người duyệt ảnh.</summary>
    [HttpPost("partner/requests/{userId:guid}/reject")]
    public async Task<IActionResult> RejectPartnerRequest(Guid userId)
    {
        var me = User.GetUserId();
        var requester = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.PendingPartnerId == me);
        if (requester is null) return NotFound();

        requester.PendingPartnerId = null;

        var myName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
        db.Notifications.Add(new Notification
        {
            SenderId = me,
            ReceiverId = userId,
            Title = "Từ chối làm partner",
            Message = $"{myName} không nhận làm partner của bạn.",
            Type = NotificationTypes.Partner,
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Phiếu đã duyệt / từ chối của người mình duyệt (kèm giờ duyệt) để xem lại.</summary>
    [HttpGet("partner/clients/{userId:guid}/reviewed")]
    public async Task<ActionResult<List<ReviewedProofDto>>> ClientReviewed(
        Guid userId, [FromQuery] string kind = "water", [FromQuery] int take = 20)
    {
        var me = User.GetUserId();
        var isClient = await db.Users.AnyAsync(u => u.Id == userId && (u.PartnerId == me || u.PendingPartnerId == me));
        if (!isClient) return Problem(statusCode: 403, title: "Bạn không phải partner của người này");
        return await social.ReviewedProofsAsync(userId, kind, take);
    }

    /// <summary>Tiến độ hôm nay của một người mình duyệt ảnh (dùng cho trang partner).</summary>
    [HttpGet("partner/clients/{userId:guid}/today")]
    public async Task<ActionResult<TodayDto>> ClientToday(Guid userId)
    {
        var me = User.GetUserId();
        var isClient = await db.Users.AnyAsync(u => u.Id == userId && u.PartnerId == me);
        if (!isClient) return Problem(statusCode: 403, title: "Bạn không phải partner của người này");
        return await water.GetTodayAsync(userId);
    }

    /// <summary>Tiến độ hôm nay của một người mình duyệt — cả 6 nhu cầu (thanh tiến độ ở trang partner).</summary>
    [HttpGet("partner/clients/{userId:guid}/needs")]
    public async Task<ActionResult<List<PartnerNeedDto>>> ClientNeeds(Guid userId)
    {
        var me = User.GetUserId();
        var isClient = await db.Users.AnyAsync(u => u.Id == userId && u.PartnerId == me);
        if (!isClient) return Problem(statusCode: 403, title: "Bạn không phải partner của người này");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var needs = new List<PartnerNeedDto>();
        int PercentOf(int done, int target) =>
            target <= 0 ? 0 : (int)Math.Round(done * 100.0 / target, MidpointRounding.AwayFromZero);

        var waterToday = await water.GetTodayAsync(userId);
        needs.Add(new("water", waterToday.TargetMl, waterToday.TotalDrankMl,
            PercentOf(waterToday.TotalDrankMl, waterToday.TargetMl),
            waterToday.PendingMl, waterToday.Logs.Count(l => l.Status == LogStatuses.Pending)));

        var settings = await db.HabitSettings.AsNoTracking().Where(h => h.UserId == userId).ToListAsync();
        var logs = await db.HabitLogs.AsNoTracking()
            .Where(l => l.UserId == userId && l.Date == today)
            .ToListAsync();

        foreach (var kind in new[] { "medicine", "meal", "study" })
        {
            var settingRow = settings.FirstOrDefault(s => s.Kind == kind);
            // Người dùng tắt nhu cầu nào thì bên partner cũng ẩn nhu cầu đó.
            if (settingRow is { Enabled: false }) continue;
            var target = settingRow?.TargetValue ?? 0;
            if (target <= 0) target = kind == "study" ? 2 : 3;
            var done = logs.Count(l => l.Kind == kind && l.Status == "approved");
            needs.Add(new(kind, target, done, PercentOf(done, target), 0,
                logs.Count(l => l.Kind == kind && l.Status == "pending"),
                settingRow?.ImageUrl, settingRow?.CutoutUrl));
        }

        // Giấc ngủ: mục tiêu là số phút ngủ trong ngày; cộng cả giấc đang chạy + chờ duyệt để partner
        // thấy % tăng dần ngay trong lúc người kia đang ngủ.
        var sleepTarget = settings.FirstOrDefault(s => s.Kind == "sleep")?.TargetValue ?? 0;
        if (sleepTarget <= 0) sleepTarget = 480;
        var userRow = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SleepDayStartAt })
            .FirstOrDefaultAsync();
        var nights = await db.SleepLogs.AsNoTracking()
            .Where(l => l.UserId == userId && l.Date >= today.AddDays(-1))
            .ToListAsync();
        var running = nights.FirstOrDefault(l => l.Status == "sleeping");
        var slept = nights
            .Where(l => l.Status != "rejected")
            .Where(l => userRow?.SleepDayStartAt is not { } boundary || l.CreatedAt >= boundary)
            .Where(l => l.Date == today || DateOnly.FromDateTime((l.StartedAtUtc ?? l.CreatedAt).ToLocalTime()) == today)
            .Sum(l => l.Status == "sleeping" && l.StartedAtUtc is { } started
                ? Math.Max(0, (int)Math.Round((DateTime.UtcNow - started).TotalMinutes))
                : l.Minutes);
        var sleepSetting = settings.FirstOrDefault(s => s.Kind == "sleep");
        if (sleepSetting is not { Enabled: false })
        {
            needs.Add(new("sleep", sleepTarget, slept, PercentOf(slept, sleepTarget), 0,
                nights.Count(l => l.Status == "pending"),
                sleepSetting?.ImageUrl, sleepSetting?.CutoutUrl,
                running is not null));
        }

        // Cân nặng: % tính từ mốc ban đầu → mục tiêu, giống trang cân nặng của họ.
        var weight = settings.FirstOrDefault(s => s.Kind == "weight");
        if (weight is not { Enabled: false })
        {
        var goal = weight?.TargetValue is > 0 ? weight.TargetValue : 60000;
        var latest = await db.HabitLogs.AsNoTracking()
            .Where(l => l.UserId == userId && l.Kind == "weight" && l.Status == "approved" && l.Value != null)
            .OrderByDescending(l => l.Date).ThenByDescending(l => l.TimeMinutes)
            .Select(l => l.Value!.Value)
            .FirstOrDefaultAsync();
        var start = weight?.StartValue is > 0 ? weight.StartValue!.Value : latest;
        var span = Math.Abs(start - goal);
        var weightPercent = Math.Abs(latest - goal) <= 200
            ? 100
            : span == 0
                ? 0
                : Math.Clamp((int)Math.Round((1 - Math.Abs(latest - goal) / (double)span) * 100), 0, 100);
        needs.Add(new("weight", goal, latest, weightPercent, 0,
            logs.Count(l => l.Kind == "weight" && l.Status == "pending"),
            weight?.ImageUrl, weight?.CutoutUrl));
        }

        return needs;
    }

    /// <summary>Partner trừ nước trong cốc của người mình duyệt ảnh (realtime + thông báo).</summary>
    [HttpPost("partner/clients/{userId:guid}/deduct")]
    public async Task<ActionResult<DeductResultDto>> Deduct(Guid userId, DeductRequest req)
    {
        var me = User.GetUserId();
        DeductResultDto result;
        try
        {
            result = await water.DeductAsync(me, userId, req.AmountMl);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Problem(statusCode: 403, title: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(statusCode: 400, title: ex.Message);
        }

        var notification = new Notification
        {
            SenderId = me,
            ReceiverId = userId,
            Title = "Bị trừ nước",
            Message = $"{result.PartnerName} đã trừ {result.AmountMl} ml trong cốc của bạn.",
            Type = NotificationTypes.Deduct
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var payload = new
        {
            notification.Id,
            result.AmountMl,
            result.TotalDrankMl,
            result.TargetMl,
            result.Percent,
            result.PartnerName,
            Title = notification.Title,
            notification.Message,
            notification.CreatedAt
        };

        var online = presence.IsOnline(userId);
        if (online)
            await hub.Clients.User(userId.ToString()).SendAsync("ReceiveDeduct", payload);

        return result;
    }

    [HttpPost("verify/{logId:int}")]
    public async Task<ActionResult<PendingProofDto>> Review(int logId, ReviewProofRequest req)
    {
        var me = User.GetUserId();
        var log = await db.WaterLogs
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Id == logId);

        if (log is null) return NotFound();
        if (log.User.PartnerId != me)
            return Problem(statusCode: 403, title: "Bạn không phải partner của người này");
        if (log.Status != LogStatuses.Pending)
            return Problem(statusCode: 400, title: "Log này đã được xử lý");

        log.Status = req.Approve ? LogStatuses.Approved : LogStatuses.Rejected;
        log.ReviewedBy = me;
        log.ReviewedAt = clock.UtcNow;

        // Lời nhắn của partner (nếu có) thay cho câu mặc định.
        var custom = string.IsNullOrWhiteSpace(req.Message) ? null : req.Message.Trim();
        var defaultMessage = req.Approve
            ? $"{log.AmountMl} ml đã được cộng vào tiến trình của bạn."
            : $"Ảnh {log.AmountMl} ml không được duyệt nên không tính vào tiến trình.";
        var message = custom ?? defaultMessage;

        db.Notifications.Add(new Notification
        {
            SenderId = me,
            ReceiverId = log.UserId,
            Title = req.Approve ? "Ảnh đã được duyệt" : "Ảnh bị từ chối",
            Message = message,
            Type = NotificationTypes.GoalAchieved,
            ImageUrl = log.ImageUrl
        });

        await db.SaveChangesAsync();

        await water.RefreshDayAsync(log.UserId, clock.ToLocalDate(log.LogTime));

        // Chỉ một sự kiện realtime cho mỗi lần duyệt/từ chối (trước đây bắn cả Nudge + Deduct → 2 toast trùng).
        if (req.Approve)
        {
            await hub.Clients.User(log.UserId.ToString()).SendAsync("ReceiveNudge", new
            {
                Title = "Ảnh đã được duyệt",
                Message = message
            });
        }
        else
        {
            var today = await water.GetTodayAsync(log.UserId);
            var partnerName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
            if (presence.IsOnline(log.UserId))
            {
                await hub.Clients.User(log.UserId.ToString()).SendAsync("ReceiveDeduct", new
                {
                    AmountMl = log.AmountMl,
                    today.TotalDrankMl,
                    today.TargetMl,
                    today.Percent,
                    PartnerName = partnerName,
                    Title = "Phiếu bị từ chối",
                    Message = message
                });
            }
        }

        return new PendingProofDto(log.Id.ToString(), log.UserId, log.User.FullName, log.User.AvatarUrl,
            log.AmountMl, log.Period, log.ImageUrl, log.Note, log.LogTime, "water", null, null, null);
    }

    [HttpPost("nudge/{userId:guid}")]
    public async Task<ActionResult<NudgeResultDto>> Nudge(Guid userId, [FromBody] NudgeRequest? req = null)
    {
        var me = User.GetUserId();
        if (!SocialService.IsValidFollow(me, userId))
            return Problem(statusCode: 400, title: "Không thể tự nhắc chính mình");

        var target = await db.Users.FindAsync(userId);
        if (target is null) return NotFound();

        // Là bạn bè (một trong hai chiều đã chấp nhận) là nhắc được — nhờ vậy partner
        // cũng nhắc được cho khách đang nhờ mình duyệt ảnh.
        var areFriends = await db.Follows.AnyAsync(f =>
            f.Status == FollowStatuses.Accepted &&
            ((f.FollowerId == me && f.FollowingId == userId) ||
             (f.FollowerId == userId && f.FollowingId == me)));
        if (!areFriends)
            return Problem(statusCode: 403, title: "Chỉ nhắc được người là bạn bè");

        var sender = await db.Users.AsNoTracking()
            .Where(u => u.Id == me)
            .Select(u => u.FullName)
            .FirstAsync();

        // Loại nhắc: mỗi nhu cầu một tiêu đề riêng, kèm lời nhắn tự soạn (nếu có).
        var (title, action) = req?.Kind switch
        {
            "meal" => ("Nhắc bạn ăn uống", "nhắc bạn ăn uống đấy!"),
            "medicine" => ("Nhắc bạn uống thuốc", "nhắc bạn uống thuốc đấy!"),
            "study" => ("Nhắc bạn học tập", "nhắc bạn học bài đấy!"),
            "sleep" => ("Nhắc bạn đi ngủ", "nhắc bạn đi ngủ đấy!"),
            "weight" => ("Nhắc bạn ghi cân nặng", "nhắc bạn ghi cân nặng đấy!"),
            _ => ("Nhắc bạn uống nước", "nhắc bạn uống nước đấy!"),
        };
        var custom = (req?.Message ?? string.Empty).Trim();
        if (custom.Length > 200) custom = custom[..200];

        var notification = new Notification
        {
            SenderId = me,
            ReceiverId = userId,
            Title = title,
            Message = custom.Length > 0 ? $"{sender}: {custom}" : $"{sender} {action}",
            Type = NotificationTypes.Nudge
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        var payload = new
        {
            notification.Id,
            Title = notification.Title,
            notification.Message,
            notification.CreatedAt
        };

        var online = presence.IsOnline(userId);
        if (online)
            await hub.Clients.User(userId.ToString()).SendAsync("ReceiveNudge", payload);

        // Push chạy nền (scope riêng) để nút nhắc phản hồi tức thì thay vì chờ dịch vụ push.
        webPush.QueueForUser(userId, notification.Title, notification.Message, ReminderStyles.Normal);

        return new NudgeResultDto(online, true);
    }
}

