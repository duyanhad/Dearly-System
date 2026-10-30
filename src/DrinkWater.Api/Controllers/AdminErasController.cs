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

/// <summary>Admin quản lý kỷ nguyên: tạo/sửa/xoá, đổi giá, tải ảnh từng cấp.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/eras")]
public class AdminErasController(
    AppDbContext db,
    IImageStorage storage,
    IOptions<StorageOptions> storageOptions,
    SystemLogWriter logs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AdminEraDto>>> List()
    {
        var eras = await db.Eras
            .AsNoTracking()
            .Include(e => e.Levels)
            .Include(e => e.Group)
            .OrderBy(e => e.Order)
            .ToListAsync();
        return eras.Select(e => e.ToAdminDto()).ToList();
    }

    /// <summary>Đổi thứ tự kỷ nguyên (kéo thả): gửi danh sách id theo thứ tự mới.</summary>
    [HttpPut("order")]
    public async Task<IActionResult> Reorder(EraOrderRequest req)
    {
        var ids = req.Ids?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
        if (ids.Count == 0) return Problem(statusCode: 400, title: "Danh sách thứ tự trống");

        var eras = await db.Eras.Where(e => ids.Contains(e.Id)).ToListAsync();
        for (var i = 0; i < ids.Count; i += 1)
        {
            var era = eras.FirstOrDefault(e => e.Id == ids[i]);
            if (era is not null) era.Order = i + 1;
        }

        await db.SaveChangesAsync();
        await logs.WriteAsync(User.GetUserId(), "EraReorder", $"Đổi thứ tự {ids.Count} kỷ nguyên");
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<AdminEraDto>> Create(AdminEraRequest req)
    {
        var error = Validate(req);
        if (error is not null) return Problem(statusCode: 400, title: error);

        var order = req.Order > 0
            ? req.Order
            : (await db.Eras.MaxAsync(e => (int?)e.Order) ?? 0) + 1;

        var era = new Era
        {
            Order = order,
            Title = req.Title.Trim(),
            Subtitle = req.Subtitle?.Trim() ?? string.Empty,
            BgFrom = string.IsNullOrWhiteSpace(req.BgFrom) ? "#f4fbff" : req.BgFrom.Trim(),
            BgTo = string.IsNullOrWhiteSpace(req.BgTo) ? "#aedcf0" : req.BgTo.Trim(),
            UnlockPrice = Math.Max(0, req.UnlockPrice),
            IsActive = req.IsActive,
            GroupId = await ResolveGroupAsync(req.GroupId),
            Levels = BuildLevels(req.Levels)
        };
        db.Eras.Add(era);
        await db.SaveChangesAsync();
        await logs.WriteAsync(User.GetUserId(), "EraCreate", $"Tạo kỷ nguyên {era.Title} (#{era.Order})");

        return era.ToAdminDto();
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AdminEraDto>> Update(Guid id, AdminEraRequest req)
    {
        var error = Validate(req);
        if (error is not null) return Problem(statusCode: 400, title: error);

        var era = await db.Eras.Include(e => e.Levels).FirstOrDefaultAsync(e => e.Id == id);
        if (era is null) return NotFound();

        era.Order = req.Order > 0 ? req.Order : era.Order;
        era.Title = req.Title.Trim();
        era.Subtitle = req.Subtitle?.Trim() ?? string.Empty;
        era.BgFrom = string.IsNullOrWhiteSpace(req.BgFrom) ? era.BgFrom : req.BgFrom.Trim();
        era.BgTo = string.IsNullOrWhiteSpace(req.BgTo) ? era.BgTo : req.BgTo.Trim();
        era.UnlockPrice = Math.Max(0, req.UnlockPrice);
        era.IsActive = req.IsActive;
        era.GroupId = await ResolveGroupAsync(req.GroupId);

        if (req.Levels is { Count: > 0 })
        {
            foreach (var incoming in req.Levels)
            {
                if (incoming.Level is < 1 or > 5) continue;
                var level = era.Levels.FirstOrDefault(l => l.Level == incoming.Level);
                if (level is null)
                {
                    level = new EraLevel { EraId = era.Id, Level = incoming.Level };
                    era.Levels.Add(level);
                }

                level.Name = string.IsNullOrWhiteSpace(incoming.Name) ? $"Cấp {incoming.Level}" : incoming.Name.Trim();
                level.Description = incoming.Description?.Trim() ?? string.Empty;
                level.Artifact = incoming.Artifact?.Trim() ?? string.Empty;
                level.Price = Math.Max(0, incoming.Price);
                if (!string.IsNullOrWhiteSpace(incoming.ImageUrl))
                    level.ImageUrl = incoming.ImageUrl.Trim();
            }
        }

        await db.SaveChangesAsync();
        await logs.WriteAsync(User.GetUserId(), "EraUpdate", $"Cập nhật kỷ nguyên {era.Title}");
        return era.ToAdminDto();
    }

    /// <summary>Xoá kỷ nguyên (kèm tiến độ của mọi người trong kỷ nguyên đó).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var era = await db.Eras.FindAsync(id);
        if (era is null) return NotFound();

        db.Eras.Remove(era);
        await db.SaveChangesAsync();
        await logs.WriteAsync(User.GetUserId(), "EraDelete", $"Xoá kỷ nguyên {era.Title}");
        return NoContent();
    }

    /// <summary>Tải ảnh linh vật cho một cấp của kỷ nguyên.</summary>
    [HttpPost("{id:guid}/levels/{level:int}/image")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<AdminEraDto>> UploadImage(Guid id, int level, IFormFile? image)
    {
        if (image is null || image.Length == 0)
            return Problem(statusCode: 400, title: "Chưa chọn ảnh");

        var era = await db.Eras.Include(e => e.Levels).FirstOrDefaultAsync(e => e.Id == id);
        if (era is null) return NotFound();

        var target = era.Levels.FirstOrDefault(l => l.Level == level);
        if (target is null) return Problem(statusCode: 400, title: "Cấp không hợp lệ");

        var (extension, error) = await ImageUpload.PrepareAsync(image, storageOptions.Value.MaxBytes);
        if (error is not null) return Problem(statusCode: 400, title: error);

        await using var stream = image.OpenReadStream();
        target.ImageUrl = await storage.SaveAsync(stream, extension!);
        await db.SaveChangesAsync();

        return era.ToAdminDto();
    }

    /// <summary>Chỉ nhận GroupId có thật, tránh lỗi khoá ngoại khi nhóm vừa bị xoá.</summary>
    private async Task<Guid?> ResolveGroupAsync(Guid? groupId)
    {
        if (groupId is null || groupId == Guid.Empty) return null;
        var exists = await db.EraGroups.AnyAsync(g => g.Id == groupId);
        return exists ? groupId : null;
    }

    private static string? Validate(AdminEraRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return "Thiếu tên kỷ nguyên";
        if (req.Title.Trim().Length > 80) return "Tên kỷ nguyên quá dài (tối đa 80 ký tự)";
        return null;
    }

    private static List<EraLevel> BuildLevels(List<AdminEraLevelRequest>? incoming)
    {
        var levels = new List<EraLevel>();
        for (var lv = 1; lv <= 5; lv += 1)
        {
            var src = incoming?.FirstOrDefault(x => x.Level == lv);
            levels.Add(new EraLevel
            {
                Level = lv,
                Name = string.IsNullOrWhiteSpace(src?.Name) ? $"Cấp {lv}" : src!.Name.Trim(),
                Description = src?.Description?.Trim() ?? string.Empty,
                Artifact = src?.Artifact?.Trim() ?? string.Empty,
                Price = Math.Max(0, src?.Price ?? 0),
                ImageUrl = string.IsNullOrWhiteSpace(src?.ImageUrl) ? null : src!.ImageUrl!.Trim()
            });
        }
        return levels;
    }
}
