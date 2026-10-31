using System.Text.RegularExpressions;
using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using DrinkWater.Api.Services;
using DrinkWater.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Controllers;

/// <summary>
/// Ghi giấc ngủ từng đêm (kèm ảnh minh chứng, partner duyệt như phiếu uống nước)
/// và tổng hợp số giờ ngủ đạt được.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sleep")]
public class SleepController(
    AppDbContext db,
    IImageStorage storage,
    IOptions<StorageOptions> storageOptions,
    RewardService rewards,
    IHubContext<NotificationHub> hub,
    IWebPushSender webPush) : ControllerBase
{
    private static readonly Regex TimePattern = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    [HttpGet]
    public async Task<ActionResult<SleepSummaryDto>> Get()
    {
        var (logs, goal) = await LoadAsync(User.GetUserId());
        return Summarize(logs, goal, await DayStartAsync(User.GetUserId()));
    }

    /// <summary>Bắt đầu giấc ngủ: chụp 1 ảnh minh chứng + hẹn giờ; bộ đếm chạy từ lúc này.</summary>
    [HttpPost("start")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<SleepSummaryDto>> Start(
        [FromForm] string bedTime,
        IFormFile? photo,
        [FromForm] int? plannedMinutes)
    {
        if (!TimePattern.IsMatch(bedTime ?? string.Empty))
            return Problem(statusCode: 400, title: "Giờ đi ngủ không hợp lệ (HH:mm)");

        var userId = User.GetUserId();

        // Đang có giấc chưa kết thúc → trả về luôn, tránh tạo trùng.
        var running = await db.SleepLogs
            .FirstOrDefaultAsync(l => l.UserId == userId && l.Status == "sleeping");
        if (running is not null)
        {
            var (current, goalNow) = await LoadAsync(userId);
            return Summarize(current, goalNow, await DayStartAsync(userId));
        }

        string? photoUrl = null;
        if (photo is { Length: > 0 })
        {
            var (extension, error) = await ImageUpload.PrepareAsync(photo, storageOptions.Value.MaxBytes);
            if (error is not null) return Problem(statusCode: 400, title: error);
            await using var stream = photo.OpenReadStream();
            photoUrl = await storage.SaveAsync(stream, extension!);
        }

        var goalMinutes = await db.HabitSettings
            .Where(h => h.UserId == userId && h.Kind == "sleep")
            .Select(h => h.TargetValue)
            .FirstOrDefaultAsync();

        var nowLocal = DateTime.Now;
        var day = DateOnly.FromDateTime(nowLocal);
        // Ngủ buổi tối (sau 12h) thuộc về ngày mai; giấc ban ngày vẫn thuộc hôm nay.
        if (ToMinutes(bedTime) >= 720 && nowLocal.Hour >= 12) day = day.AddDays(1);

        var log = new SleepLog
        {
            UserId = userId,
            Date = day,
            BedMinutes = ToMinutes(bedTime),
            WakeMinutes = 0,
            Minutes = 0,
            Status = "sleeping",
            StartedAtUtc = DateTime.UtcNow,
            PlannedMinutes = Math.Clamp(plannedMinutes ?? (goalMinutes > 0 ? goalMinutes : 480), 15, 1080)
        };
        if (photoUrl is not null) log.PhotoUrl = photoUrl;
        db.SleepLogs.Add(log);
        await db.SaveChangesAsync();

        var (logs, goal) = await LoadAsync(userId);
        return Summarize(logs, goal, await DayStartAsync(userId));
    }

    /// <summary>Dừng giấc ngủ đang chạy (kể cả dừng sớm): cần 1 ảnh minh chứng lúc thức.</summary>
    [HttpPost("{id:guid}/stop")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<SleepSummaryDto>> Stop(Guid id, IFormFile? photo, [FromForm] string? note, [FromForm] string? wakeTime)
    {
        var userId = User.GetUserId();
        var log = await db.SleepLogs.FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId);
        if (log is null) return NotFound();

        string? photoUrl = null;
        if (photo is { Length: > 0 })
        {
            var (extension, error) = await ImageUpload.PrepareAsync(photo, storageOptions.Value.MaxBytes);
            if (error is not null) return Problem(statusCode: 400, title: error);
            await using var stream = photo.OpenReadStream();
            photoUrl = await storage.SaveAsync(stream, extension!);
        }

        var started = log.StartedAtUtc ?? log.CreatedAt;
        var ended = DateTime.UtcNow;
        var minutes = (int)Math.Round((ended - started).TotalMinutes);
        if (minutes < 1) minutes = 1;
        if (minutes > 1080) minutes = 1080;

        // Giờ thức hiển thị theo giờ máy người dùng (server chạy UTC nên không dùng DateTime.Now).
        var wakeMinutes = TimePattern.IsMatch(wakeTime ?? string.Empty)
            ? ToMinutes(wakeTime!)
            : DateTime.Now.Hour * 60 + DateTime.Now.Minute;
        log.EndedAtUtc = ended;
        log.WakeMinutes = wakeMinutes;
        log.Minutes = minutes;
        if (photoUrl is not null) log.WakePhotoUrl = photoUrl;
        log.Note = (note ?? string.Empty).Trim();

        // Dừng sớm mà chưa đủ giấc → vẫn ghi nhận nhưng đánh dấu để partner xem.
        var user = await db.Users.FindAsync(userId);
        var needsReview = user?.PartnerId is not null;
        log.Status = needsReview ? "pending" : "approved";
        log.RejectReason = null;
        log.ReviewedAt = needsReview ? null : DateTime.UtcNow;

        await db.SaveChangesAsync();
        if (!needsReview) await rewards.AwardIfReachedAsync(userId, "sleep", log.Date);

        // Phiếu ngủ mới → báo partner ngay (trong app + push khi app đóng).
        if (needsReview && user?.PartnerId is { } partnerId)
        {
            var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).FirstAsync();
            var message = $"{(int)(minutes / 60.0)}h{minutes % 60:00} · đã chụp ảnh minh chứng";
            await hub.Clients.User(partnerId.ToString()).SendAsync("ReceiveProof", new
            {
                title = $"{name} vừa kết thúc giấc ngủ",
                message,
                userId
            });
            await webPush.SendToUserAsync(partnerId, $"{name} vừa kết thúc giấc ngủ", message, "normal");
        }

        var (logs, goal) = await LoadAsync(userId);
        return Summarize(logs, goal, await DayStartAsync(userId));
    }

    /// <summary>Gửi giấc ngủ đêm nay (multipart: bedTime, wakeTime, note, date?, photo?).</summary>
    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<SleepSummaryDto>> Save(
        [FromForm] string? bedTime,
        [FromForm] string wakeTime,
        [FromForm] string? note,
        [FromForm] DateOnly? date,
        IFormFile? photo,
        IFormFile? wakePhoto)
    {
        if (!TimePattern.IsMatch(wakeTime ?? string.Empty))
            return Problem(statusCode: 400, title: "Giờ thức không hợp lệ (HH:mm)");

        var userId = User.GetUserId();
        var day = date ?? DateOnly.FromDateTime(DateTime.Today);
        // Ưu tiên phiếu đang ngủ (có thể mang ngày hôm sau nếu bắt đầu trước nửa đêm).
        var existing = await db.SleepLogs.FirstOrDefaultAsync(l => l.UserId == userId && l.Status == "sleeping")
                       ?? await db.SleepLogs.FirstOrDefaultAsync(l => l.UserId == userId && l.Date == day);

        // Kết thúc giấc ngủ đã bắt đầu: giữ giờ đi ngủ (và ảnh) của bước bắt đầu.
        var sleeping = existing is { Status: "sleeping" };
        if (!sleeping && !TimePattern.IsMatch(bedTime ?? string.Empty))
            return Problem(statusCode: 400, title: "Giờ đi ngủ không hợp lệ (HH:mm)");

        var bed = sleeping ? existing!.BedMinutes : ToMinutes(bedTime!);
        var wake = ToMinutes(wakeTime);
        // Đang ngủ: tính theo mốc bắt đầu thật (server giữ), không theo giờ hẹn đi ngủ.
        var minutes = sleeping
            ? Math.Clamp((int)Math.Round((DateTime.UtcNow - (existing!.StartedAtUtc ?? existing.CreatedAt)).TotalMinutes), 1, 1080)
            : wake - bed;
        if (!sleeping)
        {
            if (minutes <= 0) minutes += 1440; // ngủ qua nửa đêm
            if (minutes > 1080) return Problem(statusCode: 400, title: "Một đêm ngủ tối đa 18 giờ");
        }

        string? photoUrl = null;
        if (photo is { Length: > 0 })
        {
            var (extension, error) = await ImageUpload.PrepareAsync(photo, storageOptions.Value.MaxBytes);
            if (error is not null) return Problem(statusCode: 400, title: error);
            await using var stream = photo.OpenReadStream();
            photoUrl = await storage.SaveAsync(stream, extension!);
        }

        string? wakePhotoUrl = null;
        if (wakePhoto is { Length: > 0 })
        {
            var (extension, error) = await ImageUpload.PrepareAsync(wakePhoto, storageOptions.Value.MaxBytes);
            if (error is not null) return Problem(statusCode: 400, title: error);
            await using var stream = wakePhoto.OpenReadStream();
            wakePhotoUrl = await storage.SaveAsync(stream, extension!);
        }

        var log = existing;
        if (log is null)
        {
            log = new SleepLog { UserId = userId, Date = day };
            db.SleepLogs.Add(log);
        }

        log.BedMinutes = bed;
        log.WakeMinutes = wake;
        log.Minutes = minutes;
        if (sleeping) log.EndedAtUtc = DateTime.UtcNow;
        log.Note = (note ?? string.Empty).Trim();
        if (photoUrl is not null) log.PhotoUrl = photoUrl;
        if (wakePhotoUrl is not null) log.WakePhotoUrl = wakePhotoUrl;

        // Có partner (người duyệt) thì phiếu luôn chờ duyệt; không có partner thì tính luôn.
        var user = await db.Users.FindAsync(userId);
        var needsReview = user?.PartnerId is not null;
        log.Status = needsReview ? "pending" : "approved";
        log.RejectReason = null;
        log.ReviewedAt = needsReview ? null : DateTime.UtcNow;

        await db.SaveChangesAsync();

        // Đủ giấc thì thưởng tiền của nhóm giấc ngủ (mỗi ngày một lần).
        if (!needsReview) await rewards.AwardIfReachedAsync(userId, "sleep", log.Date);

        var (logs, goal) = await LoadAsync(userId);
        return Summarize(logs, goal, await DayStartAsync(userId));
    }

    /// <summary>Các đêm của một tháng — dùng cho trang Lịch.</summary>
    [HttpGet("month")]
    public async Task<ActionResult<List<SleepLogDto>>> Month([FromQuery] int? year, [FromQuery] int? month)
    {
        var y = year is > 2000 and < 2200 ? year.Value : DateTime.Today.Year;
        var m = month is >= 1 and <= 12 ? month.Value : DateTime.Today.Month;

        var from = new DateOnly(y, m, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var logs = await db.SleepLogs
            .AsNoTracking()
            .Where(l => l.UserId == User.GetUserId() && l.Date >= from && l.Date <= to)
            .OrderBy(l => l.Date)
            .ToListAsync();

        return logs.Select(ToDto).ToList();
    }

    /// <summary>Phiếu giấc ngủ đang chờ mình duyệt (dành cho partner).</summary>
    [HttpGet("inbox")]
    public async Task<ActionResult<List<SleepLogDto>>> Inbox()
    {
        var userId = User.GetUserId();
        // Người nhờ mình duyệt = những user có PartnerId trỏ tới mình.
        var clientIds = await db.Users.Where(u => u.PartnerId == userId).Select(u => u.Id).ToListAsync();
        if (clientIds.Count == 0) return new List<SleepLogDto>();

        var logs = await db.SleepLogs
            .AsNoTracking()
            .Where(l => clientIds.Contains(l.UserId) && l.Status == "pending")
            .OrderBy(l => l.Date)
            .ToListAsync();

        return logs.Select(ToDto).ToList();
    }

    /// <summary>Duyệt / từ chối một phiếu giấc ngủ.</summary>
    [HttpPost("{id:guid}/review")]
    public async Task<ActionResult<SleepSummaryDto>> Review(Guid id, SleepReviewRequest req)
    {
        var userId = User.GetUserId();
        var log = await db.SleepLogs.FirstOrDefaultAsync(l => l.Id == id);
        if (log is null) return NotFound();

        var owner = await db.Users.Where(u => u.Id == log.UserId).Select(u => new { u.PartnerId }).FirstOrDefaultAsync();
        if (owner?.PartnerId != userId) return Problem(statusCode: 403, title: "Bạn không phải partner của người này");

        log.Status = req.Approve ? "approved" : "rejected";
        log.RejectReason = req.Approve ? null : (req.Reason ?? string.Empty).Trim();
        log.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (req.Approve) await rewards.AwardIfReachedAsync(log.UserId, "sleep", log.Date);

        var (logs, goal) = await LoadAsync(log.UserId);
        return Summarize(logs, goal, await DayStartAsync(log.UserId));
    }

    private async Task<(List<SleepLog> logs, int goal)> LoadAsync(Guid userId)
    {
        var since = DateOnly.FromDateTime(DateTime.Today).AddDays(-13);
        var logs = await db.SleepLogs
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.Date >= since)
            .OrderBy(l => l.Date)
            .ToListAsync();

        var goal = await db.HabitSettings
            .Where(h => h.UserId == userId && h.Kind == "sleep")
            .Select(h => h.TargetValue)
            .FirstOrDefaultAsync();

        return (logs, goal > 0 ? goal : 480);
    }

    /// <summary>Chốt ngày: đặt mốc mới để % hôm nay bắt đầu lại từ 0 (người dùng chủ động tạo ngày mới).</summary>
    [HttpPost("close-day")]
    public async Task<ActionResult<SleepSummaryDto>> CloseDay()
    {
        var userId = User.GetUserId();
        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();
        user.SleepDayStartAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var (logs, goal) = await LoadAsync(userId);
        return Summarize(logs, goal, user.SleepDayStartAt);
    }

    /// <summary>Partner chốt ngày giúp người mình theo dõi (bắt đầu ngày ngủ mới cho họ).</summary>
    [HttpPost("close-day/{userId:guid}")]
    public async Task<ActionResult<SleepSummaryDto>> CloseDayFor(Guid userId)
    {
        var me = User.GetUserId();
        var owner = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.PartnerId, u.FullName })
            .FirstOrDefaultAsync();
        if (owner is null) return NotFound();
        if (owner.PartnerId != me) return Problem(statusCode: 403, title: "Bạn không phải partner của người này");

        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();
        user.SleepDayStartAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Báo cho chủ tài khoản biết partner vừa chốt ngày giúp.
        await hub.Clients.User(userId.ToString()).SendAsync("ReceiveBroadcast", new
        {
            title = "Partner đã chốt ngày giấc ngủ",
            message = "Bạn bắt đầu một ngày ngủ mới — % và quỹ giấc ngủ đã về 0."
        });
        await webPush.SendToUserAsync(userId, "Partner đã chốt ngày giấc ngủ",
            "Bạn bắt đầu một ngày ngủ mới — % và quỹ giấc ngủ đã về 0.", "gentle");

        var (logs, goal) = await LoadAsync(userId);
        return Summarize(logs, goal, user.SleepDayStartAt);
    }

    /// <summary>Mốc "chốt ngày" của người dùng (null = chưa chốt lần nào).</summary>
    private async Task<DateTime?> DayStartAsync(Guid userId) =>
        await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.SleepDayStartAt).FirstOrDefaultAsync();

    private static SleepSummaryDto Summarize(List<SleepLog> logs, int goal, DateTime? dayStart = null)
    {
        // Chỉ tính số giờ của đêm đã được duyệt (hoặc không cần duyệt).
        var approved = logs.Where(l => l.Status == "approved").ToList();
        var last7 = approved.Where(l => l.Date >= DateOnly.FromDateTime(DateTime.Today).AddDays(-6)).ToList();

        var average = last7.Count == 0 ? 0 : (int)Math.Round(last7.Average(l => l.Minutes));
        var best = last7.Count == 0 ? 0 : last7.Max(l => l.Minutes);
        var hit = last7.Count(l => l.Minutes >= goal);

        var streak = 0;
        for (var i = last7.Count - 1; i >= 0; i -= 1)
        {
            if (last7[i].Minutes >= goal) streak += 1;
            else break;
        }

        // Tổng ngủ hôm nay: cộng mọi giấc trong ngày (kể cả đang chạy và CHỜ DUYỆT — chỉ bỏ phiếu bị
        // từ chối). Nhờ vậy kết thúc giấc không bị reset về 0 trong lúc chờ partner.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var minutesToday = 0;
        foreach (var log in logs)
        {
            if (log.Status == "rejected") continue;
            var startedLocal = (log.StartedAtUtc ?? log.CreatedAt).ToLocalTime();
            var belongsToday = log.Date == today || DateOnly.FromDateTime(startedLocal) == today;
            if (!belongsToday) continue;
            if (dayStart is { } boundary && log.CreatedAt < boundary) continue; // đã chốt ngày → bỏ giấc cũ
            minutesToday += log.Status == "sleeping" && log.StartedAtUtc is { } started
                ? Math.Max(0, (int)Math.Round((DateTime.UtcNow - started).TotalMinutes))
                : log.Minutes;
        }
        var percentToday = goal > 0 ? Math.Min(100, (int)Math.Round(minutesToday * 100.0 / goal)) : 0;

        return new SleepSummaryDto(
            goal,
            average,
            best,
            last7.Count,
            hit,
            streak,
            logs.OrderBy(l => l.Date).Select(ToDto).ToList(),
            minutesToday,
            percentToday);
    }

    private static SleepLogDto ToDto(SleepLog l) => new(
        l.Id,
        l.Date,
        ToText(l.BedMinutes),
        ToText(l.WakeMinutes),
        l.Minutes,
        l.PhotoUrl,
        l.WakePhotoUrl,
        l.Note,
        l.Status,
        l.RejectReason,
        l.StartedAtUtc,
        l.EndedAtUtc,
        l.PlannedMinutes);

    private static int ToMinutes(string hhmm)
    {
        var parts = hhmm.Split(':');
        return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
    }

    private static string ToText(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}
