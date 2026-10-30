using System.Text.RegularExpressions;
using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using DrinkWater.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    RewardService rewards) : ControllerBase
{
    private static readonly Regex TimePattern = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    [HttpGet]
    public async Task<ActionResult<SleepSummaryDto>> Get()
    {
        var (logs, goal) = await LoadAsync(User.GetUserId());
        return Summarize(logs, goal);
    }

    /// <summary>Bắt đầu giấc ngủ: giờ đi ngủ + ảnh minh chứng (multipart: bedTime, photo?).</summary>
    [HttpPost("start")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<SleepSummaryDto>> Start([FromForm] string bedTime, IFormFile? photo)
    {
        if (!TimePattern.IsMatch(bedTime ?? string.Empty))
            return Problem(statusCode: 400, title: "Giờ đi ngủ không hợp lệ (HH:mm)");

        var userId = User.GetUserId();
        string? photoUrl = null;
        if (photo is { Length: > 0 })
        {
            var (extension, error) = await ImageUpload.PrepareAsync(photo, storageOptions.Value.MaxBytes);
            if (error is not null) return Problem(statusCode: 400, title: error);
            await using var stream = photo.OpenReadStream();
            photoUrl = await storage.SaveAsync(stream, extension!);
        }

        var nowLocal = DateTime.Now;
        var day = DateOnly.FromDateTime(nowLocal);
        // Đêm bắt đầu buổi tối thuộc về ngày thức dậy: bắt đầu sau 12h → ngày mai;
        // còn bấm "bắt đầu" lúc rạng sáng cho giờ ngủ tối hôm trước → vẫn thuộc hôm nay.
        if (ToMinutes(bedTime) >= 720 && nowLocal.Hour >= 12) day = day.AddDays(1);
        var log = await db.SleepLogs.FirstOrDefaultAsync(l => l.UserId == userId && l.Date == day);
        if (log is null)
        {
            log = new SleepLog { UserId = userId, Date = day };
            db.SleepLogs.Add(log);
        }

        // Chưa biết giờ thức nên số phút = 0; phiếu ở trạng thái "sleeping" (không tính vào mục tiêu).
        log.BedMinutes = ToMinutes(bedTime);
        log.WakeMinutes = 0;
        log.Minutes = 0;
        if (photoUrl is not null) log.PhotoUrl = photoUrl;
        log.Status = "sleeping";
        log.RejectReason = null;
        log.ReviewedAt = null;

        await db.SaveChangesAsync();

        var (logs, goal) = await LoadAsync(userId);
        return Summarize(logs, goal);
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
        var minutes = wake - bed;
        if (minutes <= 0) minutes += 1440; // ngủ qua nửa đêm
        if (minutes > 1080) return Problem(statusCode: 400, title: "Một đêm ngủ tối đa 18 giờ");

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
        return Summarize(logs, goal);
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
        return Summarize(logs, goal);
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

    private static SleepSummaryDto Summarize(List<SleepLog> logs, int goal)
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

        return new SleepSummaryDto(
            goal,
            average,
            best,
            last7.Count,
            hit,
            streak,
            logs.OrderBy(l => l.Date).Select(ToDto).ToList());
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
        l.RejectReason);

    private static int ToMinutes(string hhmm)
    {
        var parts = hhmm.Split(':');
        return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
    }

    private static string ToText(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}
