using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Hubs;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using DrinkWater.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/user")]
public class UserController(
    AppDbContext db,
    IClock clock,
    IImageStorage storage,
    IOptions<StorageOptions> storageOptions,
    IHubContext<NotificationHub> hub) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<UserProfileDto>> Profile()
    {
        var user = await db.Users.FindAsync(User.GetUserId());
        return user is null ? NotFound() : user.ToProfile();
    }

    [HttpPut("profile")]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile(UpdateProfileRequest req)
    {
        var user = await db.Users.FindAsync(User.GetUserId());
        if (user is null) return NotFound();

        user.FullName = req.FullName.Trim();
        await db.SaveChangesAsync();
        return user.ToProfile();
    }

    /// <summary>Ảnh cá nhân (avatar). Ghi đè URL cũ, file cũ để lại trên đĩa.</summary>
    [HttpPut("avatar")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<UserProfileDto>> UpdateAvatar(IFormFile image)
    {
        var user = await db.Users.FindAsync(User.GetUserId());
        if (user is null) return NotFound();

        var (extension, error) = await ImageUpload.PrepareAsync(image, storageOptions.Value.MaxBytes);
        if (error is not null) return Problem(statusCode: 400, title: error);

        await using var stream = image.OpenReadStream();
        user.AvatarUrl = await storage.SaveAsync(stream, extension!);
        await db.SaveChangesAsync();

        return user.ToProfile();
    }

    /// <summary>Tuỳ chọn cá nhân: bắt buộc chụp ảnh khi thêm nước + nhắc uống nước tự động.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<UserProfileDto>> UpdateSettings(UpdateSettingsRequest req)
    {
        var user = await db.Users.FindAsync(User.GetUserId());
        if (user is null) return NotFound();

        if (req.RequirePhoto is { } requirePhoto) user.RequirePhoto = requirePhoto;
        if (req.ReminderEnabled is { } reminderEnabled) user.ReminderEnabled = reminderEnabled;
        if (req.ReminderStyle is { } style)
        {
            if (!ReminderStyles.IsValid(style))
                return Problem(statusCode: 400, title: "Kiểu nhắc không hợp lệ");
            user.ReminderStyle = style;
        }
        if (req.ReminderIntervalMinutes is { } interval)
        {
            if (interval is < 15 or > 480)
                return Problem(statusCode: 400, title: "Khoảng cách nhắc phải từ 15 phút đến 8 giờ");
            user.ReminderIntervalMinutes = interval;
        }
        if (req.ReminderMl is { } ml)
        {
            if (ml is < 50 or > 2000)
                return Problem(statusCode: 400, title: "Số ml mỗi lần uống phải từ 50 đến 2000");
            user.ReminderMl = ml;
        }
        if (req.ReminderStartHour is { } startHour)
        {
            if (startHour is < 0 or > 23)
                return Problem(statusCode: 400, title: "Giờ bắt đầu phải từ 0 đến 23");
            user.ReminderStartHour = startHour;
        }
        if (req.ReminderEndHour is { } endHour)
        {
            if (endHour is < 1 or > 24)
                return Problem(statusCode: 400, title: "Giờ kết thúc phải từ 1 đến 24");
            user.ReminderEndHour = endHour;
        }
        if (user.ReminderStartHour >= user.ReminderEndHour)
            return Problem(statusCode: 400, title: "Giờ kết thúc phải sau giờ bắt đầu");
        await db.SaveChangesAsync();

        return user.ToProfile();
    }

    /// <summary>Ảnh bìa trang cá nhân: tải ảnh lên, hoặc chọn nền dựng sẵn ("preset:ocean").</summary>
    [HttpPut("cover")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<UserProfileDto>> UpdateCover(IFormFile? image, [FromForm] string? preset)
    {
        var user = await db.Users.FindAsync(User.GetUserId());
        if (user is null) return NotFound();

        if (image is { Length: > 0 })
        {
            var (extension, error) = await ImageUpload.PrepareAsync(image, storageOptions.Value.MaxBytes);
            if (error is not null) return Problem(statusCode: 400, title: error);

            await using var stream = image.OpenReadStream();
            user.CoverUrl = await storage.SaveAsync(stream, extension!);
        }
        else if (!string.IsNullOrWhiteSpace(preset))
        {
            if (!CoverPresets.IsValid(preset))
                return Problem(statusCode: 400, title: "Nền không hợp lệ");
            user.CoverUrl = $"preset:{preset}";
        }
        else
        {
            user.CoverUrl = null;
        }

        await db.SaveChangesAsync();
        return user.ToProfile();
    }

    [HttpGet("stats")]
    public async Task<ActionResult<UserStatsDto>> Stats()
    {
        var me = User.GetUserId();
        var user = await db.Users.FindAsync(me);
        if (user is null) return NotFound();

        var logs = await db.WaterLogs.AsNoTracking().Where(l => l.UserId == me).ToListAsync();
        var summaries = await db.DailySummaries.AsNoTracking()
            .Where(s => s.UserId == me)
            .OrderByDescending(s => s.Date)
            .ToListAsync();

        var streak = 0;
        var cursor = clock.TodayLocal;
        var completedToday = summaries.FirstOrDefault(s => s.Date == cursor && s.IsCompleted);
        if (completedToday is null) cursor = cursor.AddDays(-1);

        foreach (var summary in summaries.Where(s => s.Date <= cursor).OrderByDescending(s => s.Date))
        {
            if (summary.Date != cursor || !summary.IsCompleted) break;
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return new UserStatsDto(
            logs.Count,
            logs.Where(l => LogStatuses.Counted.Contains(l.Status)).Sum(l => l.AmountMl),
            streak,
            summaries.Count(s => s.IsCompleted),
            user.CreatedAt);
    }

    [HttpPut("targets")]
    public async Task<ActionResult<UserProfileDto>> UpdateTargets(UpdateTargetsRequest req)
    {
        if (req.MorningTargetMl + req.AfternoonTargetMl + req.EveningTargetMl != req.TargetWaterMl)
            return Problem(statusCode: 400,
                title: "Tổng 3 ca phải bằng mục tiêu ngày",
                detail: $"Sáng + Trưa + Tối = {req.MorningTargetMl + req.AfternoonTargetMl + req.EveningTargetMl}, mục tiêu là {req.TargetWaterMl}");

        var user = await db.Users.FindAsync(User.GetUserId());
        if (user is null) return NotFound();

        user.TargetWaterMl = req.TargetWaterMl;
        user.MorningTargetMl = req.MorningTargetMl;
        user.AfternoonTargetMl = req.AfternoonTargetMl;
        user.EveningTargetMl = req.EveningTargetMl;
        await db.SaveChangesAsync();

        return user.ToProfile();
    }

    /// <summary>Chọn người duyệt ảnh minh chứng của mình; null = tắt duyệt.</summary>
    [HttpPut("partner")]
    public async Task<ActionResult<UserProfileDto>> SetPartner(SetPartnerRequest req)
    {
        var me = User.GetUserId();
        var user = await db.Users.FindAsync(me);
        if (user is null) return NotFound();

        if (req.PartnerId is { } partnerId)
        {
            if (partnerId == me)
                return Problem(statusCode: 400, title: "Không thể tự chọn mình làm partner");

            // Chỉ cần là bạn bè (một trong hai chiều đã chấp nhận) là chọn được làm partner.
            var isFriend = await db.Follows.AnyAsync(f =>
                f.Status == FollowStatuses.Accepted &&
                ((f.FollowerId == partnerId && f.FollowingId == me) ||
                 (f.FollowerId == me && f.FollowingId == partnerId)));
            if (!isFriend)
                return Problem(statusCode: 400, title: "Partner phải là bạn bè của bạn (đã kết bạn)");

            if (user.PartnerId != partnerId)
            {
                var myName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
                db.Notifications.Add(new Notification
                {
                    SenderId = me,
                    ReceiverId = partnerId,
                    Title = "Xin bạn duyệt ảnh cốc nước",
                    Message = $"{myName} nhờ bạn duyệt ảnh cốc nước. Mở Partner để đồng ý.",
                    Type = NotificationTypes.Partner,
                });
                // Chỉ là lời mời: người kia phải đồng ý thì mới thành người duyệt.
                user.PendingPartnerId = partnerId;
                await db.SaveChangesAsync();
                await hub.Clients.User(partnerId.ToString()).SendAsync("ReceiveNudge", new
                {
                    Title = "Xin bạn duyệt ảnh cốc nước",
                    Message = $"{myName} nhờ bạn duyệt ảnh cốc nước. Mở Partner để đồng ý.",
                });
                return user.ToProfile();
            }

            // Đã là người duyệt của nhau rồi: không tạo lời mời mới.
            user.PendingPartnerId = null;
            await db.SaveChangesAsync();
            return user.ToProfile();
        }

        // Bỏ chọn: xoá cả người duyệt đã đồng ý lẫn lời mời đang chờ.
        user.PartnerId = null;
        user.PendingPartnerId = null;
        await db.SaveChangesAsync();
        return user.ToProfile();
    }

    [HttpPut("fcm-token")]
    public async Task<IActionResult> UpdateFcmToken(FcmTokenRequest req)
    {
        var user = await db.Users.FindAsync(User.GetUserId());
        if (user is null) return NotFound();

        user.FCMToken = req.Token;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<UserSummaryDto>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return new List<UserSummaryDto>();

        var me = User.GetUserId();
        var term = q.Trim().ToLowerInvariant();

        var rows = await db.Users
            .Where(u => u.Id != me && (u.Email.Contains(term) || u.FullName.ToLower().Contains(term)))
            .OrderBy(u => u.FullName)
            .Take(20)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                IsFollowing = db.Follows.Any(f => f.FollowerId == me && f.FollowingId == u.Id),
                FollowStatus = db.Follows.Where(f => f.FollowerId == me && f.FollowingId == u.Id)
                    .Select(f => f.Status).FirstOrDefault() ?? "None",
                IsMyPartner = u.PartnerId == me,
                Nickname = db.FriendNicknames.Where(n => n.OwnerId == me && n.TargetId == u.Id)
                    .Select(n => n.Nickname).FirstOrDefault(),
                u.AvatarUrl
            })
            .ToListAsync();

        // Không trả email đầy đủ cho người lạ (chống thu thập email hàng loạt).
        // Chỉ hiện đầy đủ khi người dùng gõ đúng cả địa chỉ (họ đã biết email đó) hoặc đã là bạn bè.
        return rows.Select(u => new UserSummaryDto(
                u.Id,
                u.Email.Equals(term, StringComparison.OrdinalIgnoreCase) || u.IsFollowing
                    ? u.Email
                    : MaskEmail(u.Email),
                u.FullName,
                u.IsFollowing,
                u.FollowStatus,
                u.IsMyPartner,
                u.Nickname,
                u.AvatarUrl))
            .ToList();
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        var name = email[..at];
        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        var maskedName = name.Length <= 2 ? name[..1] + "***" : name[..2] + "***";
        var maskedDomain = dot > 0
            ? domain[..1] + "***" + domain[dot..]
            : domain[..1] + "***";
        return $"{maskedName}@{maskedDomain}";
    }
}

