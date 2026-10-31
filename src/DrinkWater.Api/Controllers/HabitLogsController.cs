using System.Text.RegularExpressions;
using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using DrinkWater.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Controllers;

public record HabitLogDto(
    Guid Id,
    string Kind,
    DateOnly Date,
    string Time,
    string Note,
    string? PhotoUrl,
    string Status,
    string? RejectReason,
    int? Value,
    string? MealSlot = null,
    DateTime? CreatedAt = null,
    DateTime? ReviewedAt = null);

public record HabitDayDto(string Kind, int Target, int Done, List<HabitLogDto> Logs);

public record HabitLogRequest(DateTime? Date);

/// <summary>Ghi nhận theo lần cho uống thuốc / bữa ăn / học (ảnh minh chứng do partner duyệt).</summary>
[ApiController]
[Authorize]
[Route("api/habits")]
public class HabitLogsController(
    AppDbContext db,
    IImageStorage storage,
    IOptions<StorageOptions> storageOptions,
    RewardService rewards) : ControllerBase
{
    private static readonly Regex TimePattern = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    /// <summary>Tình hình hôm nay của một nhu cầu (mục tiêu, đã ghi, danh sách lần ghi).</summary>
    [HttpGet("{kind}/today")]
    public async Task<ActionResult<HabitDayDto>> Today(string kind)
    {
        if (!HabitSetting.LogKinds.Contains(kind)) return Problem(statusCode: 400, title: "Nhu cầu không hợp lệ");

        var userId = User.GetUserId();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var logs = await db.HabitLogs
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.Kind == kind && l.Date == today)
            .OrderBy(l => l.TimeMinutes)
            .ToListAsync();

        var target = await db.HabitSettings
            .Where(h => h.UserId == userId && h.Kind == kind)
            .Select(h => h.TargetValue)
            .FirstOrDefaultAsync();

        // Chưa cấu hình thì dùng mục tiêu mặc định để thanh tiến độ vẫn có nghĩa.
        if (target <= 0) target = kind switch { "medicine" => 3, "meal" => 3, "study" => 2, "weight" => 60000, _ => 3 };

        // Chỉ phiếu đã duyệt mới được cộng vào chỉ tiêu (phiếu chờ duyệt hiện riêng).
        // Bữa ăn: chỉ 3 bữa chính (sáng/trưa/tối) được tính, mỗi bữa tối đa 1 lần/ngày;
        // bữa phụ / ăn vặt vẫn ghi nhận nhưng không cộng %.
        var done = kind == "meal"
            ? MealSlots.Count(slot => logs.Any(l => l.MealSlot == slot && l.Status == "approved"))
            : logs.Count(l => l.Status == "approved");
        return new HabitDayDto(kind, target, done, logs.Select(ToDto).ToList());
    }

    /// <summary>Ba bữa chính được tính % (bữa phụ/ăn vặt chỉ là ghi nhận).</summary>
    private static readonly string[] MealSlots = ["breakfast", "lunch", "dinner"];

    /// <summary>Ghi một lần (multipart: time, note, photo?, value?) — value chỉ dùng cho cân nặng (gam).</summary>
    [HttpPost("{kind}/logs")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<HabitDayDto>> Add(
        string kind,
        [FromForm] string time,
        [FromForm] string? note,
        [FromForm] int? value,
        [FromForm] DateOnly? date,
        [FromForm] string? mealSlot,
        IFormFile? photo)
    {
        if (!HabitSetting.LogKinds.Contains(kind)) return Problem(statusCode: 400, title: "Nhu cầu không hợp lệ");
        if (!TimePattern.IsMatch(time ?? string.Empty)) return Problem(statusCode: 400, title: "Giờ không hợp lệ (HH:mm)");
        // Cân nặng: 20–300 kg, lưu bằng gam.
        if (kind == "weight" && (value is null or < 20000 or > 300000))
            return Problem(statusCode: 400, title: "Cân nặng không hợp lệ (20–300 kg)");

        var userId = User.GetUserId();
        string? photoUrl = null;
        if (photo is { Length: > 0 })
        {
            var (extension, error) = await ImageUpload.PrepareAsync(photo, storageOptions.Value.MaxBytes);
            if (error is not null) return Problem(statusCode: 400, title: error);
            await using var stream = photo.OpenReadStream();
            photoUrl = await storage.SaveAsync(stream, extension!);
        }

        var user = await db.Users.FindAsync(userId);
        // Có partner (người duyệt) thì mọi lần ghi đều chờ partner duyệt, ảnh chỉ là minh chứng thêm.
        var needsReview = user?.PartnerId is not null;

        var log = new HabitLog
        {
            UserId = userId,
            Kind = kind,
            Date = date ?? DateOnly.FromDateTime(DateTime.Today),
            TimeMinutes = ToMinutes(time),
            Note = (note ?? string.Empty).Trim(),
            PhotoUrl = photoUrl,
            Status = needsReview ? "pending" : "approved",
            ReviewedAt = needsReview ? null : DateTime.UtcNow,
            Value = kind == "weight" ? value : null,
            MealSlot = kind == "meal" ? NormalizeMealSlot(mealSlot) : null,
        };
        db.HabitLogs.Add(log);

        // Cân nặng: chốt mốc ban đầu cố định ở lần cân đầu tiên (không đổi dù xoá log).
        if (kind == "weight" && value is not null)
        {
            var setting = await db.HabitSettings.FirstOrDefaultAsync(h => h.UserId == userId && h.Kind == "weight");
            if (setting is null)
            {
                setting = HabitSetting.Default(userId, "weight");
                db.HabitSettings.Add(setting);
            }
            if (setting.StartValue is null)
            {
                setting.StartValue = value;
                setting.StartDate = log.Date;
            }
        }

        await db.SaveChangesAsync();

        // Đạt mục tiêu ngày thì thưởng tiền của nhóm nhu cầu (mỗi ngày một lần).
        if (log.Status == "approved") await rewards.AwardIfReachedAsync(userId, kind, log.Date);

        return await Today(kind);
    }

    /// <summary>Lịch sử ghi nhận gần đây (cân nặng cần chuỗi số liệu để vẽ tiến trình).</summary>
    [HttpGet("{kind}/history")]
    public async Task<ActionResult<List<HabitLogDto>>> History(string kind, [FromQuery] int limit = 30)
    {
        if (!HabitSetting.LogKinds.Contains(kind)) return Problem(statusCode: 400, title: "Nhu cầu không hợp lệ");
        var userId = User.GetUserId();
        var take = Math.Clamp(limit, 1, 200);

        var logs = await db.HabitLogs
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.Kind == kind)
            .OrderByDescending(l => l.Date).ThenByDescending(l => l.TimeMinutes).ThenByDescending(l => l.CreatedAt)
            .Take(take)
            .ToListAsync();

        return logs.Select(ToDto).ToList();
    }

    /// <summary>Xoá một lần ghi hôm nay (chỉ của chính mình).</summary>
    [HttpDelete("logs/{id:guid}")]
    public async Task<ActionResult<HabitDayDto>> Remove(Guid id)
    {
        var userId = User.GetUserId();
        var log = await db.HabitLogs.FirstOrDefaultAsync(l => l.Id == id && l.UserId == userId);
        if (log is null) return NotFound();

        db.HabitLogs.Remove(log);
        await db.SaveChangesAsync();
        return await Today(log.Kind);
    }

    /// <summary>Phiếu đang chờ mình duyệt (thuốc / ăn / học của người nhờ mình).</summary>
    [HttpGet("logs/inbox")]
    public async Task<ActionResult<List<HabitLogDto>>> Inbox()
    {
        var userId = User.GetUserId();
        var clientIds = await db.Users.Where(u => u.PartnerId == userId).Select(u => u.Id).ToListAsync();
        if (clientIds.Count == 0) return new List<HabitLogDto>();

        var logs = await db.HabitLogs
            .AsNoTracking()
            .Where(l => clientIds.Contains(l.UserId) && l.Status == "pending")
            .OrderBy(l => l.Date)
            .ToListAsync();

        return logs.Select(ToDto).ToList();
    }

    [HttpPost("logs/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, [FromBody] ReviewBody body)
    {
        var userId = User.GetUserId();
        var log = await db.HabitLogs.FirstOrDefaultAsync(l => l.Id == id);
        if (log is null) return NotFound();

        var owner = await db.Users.Where(u => u.Id == log.UserId).Select(u => new { u.PartnerId }).FirstOrDefaultAsync();
        if (owner?.PartnerId != userId) return Problem(statusCode: 403, title: "Bạn không phải partner của người này");

        log.Status = body.Approve ? "approved" : "rejected";
        log.RejectReason = body.Approve ? null : (body.Reason ?? string.Empty).Trim();
        log.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Partner duyệt xong mà đủ mục tiêu thì người ghi được thưởng.
        if (body.Approve) await rewards.AwardIfReachedAsync(log.UserId, log.Kind, log.Date);
        return NoContent();
    }

    public record ReviewBody(bool Approve, string? Reason);

    private static HabitLogDto ToDto(HabitLog l) => new(
        l.Id,
        l.Kind,
        l.Date,
        $"{l.TimeMinutes / 60:00}:{l.TimeMinutes % 60:00}",
        l.Note,
        l.PhotoUrl,
        l.Status,
        l.RejectReason,
        l.Value,
        l.MealSlot,
        l.CreatedAt,
        l.ReviewedAt);

    /// <summary>Chuẩn hoá loại bữa; giá trị lạ thì coi là bữa chính theo giờ (mặc định "lunch").</summary>
    private static string NormalizeMealSlot(string? slot) => slot?.Trim().ToLowerInvariant() switch
    {
        "breakfast" or "lunch" or "dinner" or "snack" or "extra" => slot!.Trim().ToLowerInvariant(),
        _ => "lunch"
    };

    private static int ToMinutes(string hhmm)
    {
        var parts = hhmm.Split(':');
        return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
    }
}
