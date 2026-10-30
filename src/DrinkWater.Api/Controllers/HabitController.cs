using System.Text.RegularExpressions;
using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

/// <summary>
/// Cài đặt nhắc cho uống thuốc / bữa ăn / giấc ngủ. Chỉ lưu cấu hình để các tính năng nhắc
/// (và kỷ nguyên theo nhóm) dùng lại — chưa tự bắn thông báo.
/// </summary>
[ApiController]
[Authorize]
[Route("api/habits")]
public class HabitController(
    AppDbContext db,
    DrinkWater.Api.Storage.IImageStorage storage,
    DrinkWater.Api.Services.RembgClient rembg,
    Microsoft.Extensions.Options.IOptions<DrinkWater.Api.Storage.StorageOptions> storageOptions) : ControllerBase
{
    private static readonly Regex TimePattern = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    [HttpGet]
    public async Task<ActionResult<List<HabitSettingDto>>> Get()
    {
        var userId = User.GetUserId();
        var rows = await db.HabitSettings.Where(h => h.UserId == userId).ToListAsync();

        var missing = HabitSetting.Kinds
            .Where(k => rows.All(r => r.Kind != k))
            .Select(k => HabitSetting.Default(userId, k))
            .ToList();

        if (missing.Count > 0)
        {
            db.HabitSettings.AddRange(missing);
            await db.SaveChangesAsync();
            rows.AddRange(missing);
        }

        return rows
            .OrderBy(r => Array.IndexOf(HabitSetting.Kinds, r.Kind))
            .Select(ToDto)
            .ToList();
    }

    [HttpPut("{kind}")]
    public async Task<ActionResult<HabitSettingDto>> Save(string kind, HabitSettingRequest req)
    {
        if (!HabitSetting.Kinds.Contains(kind)) return Problem(statusCode: 400, title: "Nhu cầu không hợp lệ");

        var times = (req.Times ?? [])
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => t.Length > 0)
            .ToList();

        if (times.Count > 8) return Problem(statusCode: 400, title: "Tối đa 8 mốc giờ");
        var bad = times.FirstOrDefault(t => !TimePattern.IsMatch(t));
        if (bad is not null) return Problem(statusCode: 400, title: $"Giờ \"{bad}\" không hợp lệ (định dạng HH:mm)");

        // weight: mục tiêu lưu bằng gam (20 000–300 000 gam = 20–300 kg)
        var maxTarget = kind switch { "sleep" => 1440, "weight" => 300000, _ => 12 };
        var minTarget = kind == "weight" ? 20000 : 0;
        if (req.TargetValue < minTarget || req.TargetValue > maxTarget)
            return Problem(statusCode: 400, title: kind == "weight"
                ? "Mục tiêu cân nặng phải trong khoảng 20–300 kg"
                : $"Giá trị mục tiêu phải trong khoảng 0–{maxTarget}");

        var userId = User.GetUserId();
        var row = await db.HabitSettings.FirstOrDefaultAsync(h => h.UserId == userId && h.Kind == kind);
        if (row is null)
        {
            row = new HabitSetting { UserId = userId, Kind = kind };
            db.HabitSettings.Add(row);
        }

        row.Enabled = req.Enabled;
        row.Times = string.Join(",", times);
        row.TargetValue = req.TargetValue;
        row.RemindBeforeMin = Math.Clamp(req.RemindBeforeMin, 0, 240);
        row.Note = (req.Note ?? string.Empty).Trim();
        if (req.AlarmEnabled is not null) row.AlarmEnabled = req.AlarmEnabled.Value;
        if (req.SnoozeMinutes is not null) row.SnoozeMinutes = Math.Clamp(req.SnoozeMinutes.Value, 0, 60);
        if (!string.IsNullOrWhiteSpace(req.AlarmMode))
            row.AlarmMode = new[] { "gentle", "insist", "crazy" }.Contains(req.AlarmMode) ? req.AlarmMode : "gentle";
        if (req.AlarmRepeatPerRound is not null) row.AlarmRepeatPerRound = Math.Clamp(req.AlarmRepeatPerRound.Value, 1, 10);
        if (req.AlarmRounds is not null) row.AlarmRounds = Math.Clamp(req.AlarmRounds.Value, 1, 10);
        if (req.AlarmIntervalMin is not null) row.AlarmIntervalMin = Math.Clamp(req.AlarmIntervalMin.Value, 1, 30);
        if (req.LockUntilSettings is not null) row.LockUntilSettings = req.LockUntilSettings.Value;
        if (kind == "weight" && req.Direction is not null)
            row.Direction = req.Direction is "up" or "down" ? req.Direction : null;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return ToDto(row);
    }

    /// <summary>Tải ảnh "vật thể" của nhu cầu (server tự tách nền để làm hình chính).</summary>
    [HttpPost("{kind}/image")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<HabitSettingDto>> UploadImage(string kind, IFormFile? image)
    {
        if (!HabitSetting.Kinds.Contains(kind)) return Problem(statusCode: 400, title: "Nhu cầu không hợp lệ");
        if (image is null || image.Length == 0) return Problem(statusCode: 400, title: "Chưa chọn ảnh");

        var (extension, error) = await DrinkWater.Api.Storage.ImageUpload.PrepareAsync(image, storageOptions.Value.MaxBytes);
        if (error is not null) return Problem(statusCode: 400, title: error);

        var userId = User.GetUserId();
        var row = await db.HabitSettings.FirstOrDefaultAsync(h => h.UserId == userId && h.Kind == kind);
        if (row is null)
        {
            row = HabitSetting.Default(userId, kind);
            db.HabitSettings.Add(row);
        }

        await using (var stream = image.OpenReadStream())
            row.ImageUrl = await storage.SaveAsync(stream, extension!);

        row.CutoutUrl = null;
        await db.SaveChangesAsync();

        using (var buffer = new MemoryStream())
        {
            await using (var source = image.OpenReadStream())
                await source.CopyToAsync(buffer);

            var cut = await rembg.RemoveBackgroundAsync(buffer.ToArray(), image.FileName, HttpContext.RequestAborted);
            if (cut is not null)
            {
                using var cutStream = new MemoryStream(cut);
                row.CutoutUrl = await storage.SaveAsync(cutStream, ".png");
                await db.SaveChangesAsync();
            }
        }

        return ToDto(row);
    }

    [HttpDelete("{kind}/image")]
    public async Task<ActionResult<HabitSettingDto>> RemoveImage(string kind)
    {
        var userId = User.GetUserId();
        var row = await db.HabitSettings.FirstOrDefaultAsync(h => h.UserId == userId && h.Kind == kind);
        if (row is null) return NotFound();

        row.ImageUrl = null;
        row.CutoutUrl = null;
        await db.SaveChangesAsync();
        return ToDto(row);
    }

    private static HabitSettingDto ToDto(HabitSetting h) => new(
        h.Kind,
        h.Enabled,
        h.Times.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        h.TargetValue,
        h.RemindBeforeMin,
        h.Note,
        h.AlarmEnabled,
        h.SnoozeMinutes,
        h.AlarmMode,
        h.AlarmRepeatPerRound,
        h.AlarmRounds,
        h.AlarmIntervalMin,
        h.LockUntilSettings,
        h.ImageUrl,
        h.CutoutUrl,
        h.Direction,
        h.StartValue,
        h.StartDate);
}
