using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using DrinkWater.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

/// <summary>
/// Video ngắn gửi kèm khi nhắc nhở: admin tải lên / bật-tắt / xoá theo từng nhu cầu,
/// người dùng chỉ đọc danh sách video đang bật để xem trước trong hộp nhắc.
/// </summary>
[ApiController]
[Route("api/nudge-videos")]
public class NudgeVideosController(AppDbContext db) : ControllerBase
{
    private static readonly string[] Kinds = ["water", "meal", "medicine", "study", "sleep", "weight"];
    private const long MaxVideoBytes = 12 * 1024 * 1024;

    /// <summary>Video đang bật (mọi người dùng đã đăng nhập đều xem trước được).</summary>
    [HttpGet]
    [Authorize]
    public async Task<List<NudgeVideoDto>> List() =>
        await db.NudgeVideos
            .AsNoTracking()
            .Where(v => v.IsActive)
            .OrderBy(v => v.Kind).ThenBy(v => v.Id)
            .Select(v => new NudgeVideoDto(v.Id, v.Kind, v.Url, v.Label, v.IsActive, v.CreatedAt))
            .ToListAsync();

    /// <summary>Tất cả video (kể cả đang tắt) cho trang admin.</summary>
    [HttpGet("/api/admin/nudge-videos")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<List<NudgeVideoDto>> AdminList() =>
        await db.NudgeVideos
            .AsNoTracking()
            .OrderBy(v => v.Kind).ThenBy(v => v.Id)
            .Select(v => new NudgeVideoDto(v.Id, v.Kind, v.Url, v.Label, v.IsActive, v.CreatedAt))
            .ToListAsync();

    /// <summary>Tải video mới (multipart: kind, label?, file).</summary>
    [HttpPost("/api/admin/nudge-videos")]
    [Authorize(Roles = Roles.Admin)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxVideoBytes + 1024 * 1024)]
    public async Task<ActionResult<NudgeVideoDto>> Upload(
        [FromForm] string kind,
        [FromForm] string? label,
        IFormFile? file,
        [FromServices] IImageStorage storage,
        CancellationToken ct)
    {
        var cleanKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (!Kinds.Contains(cleanKind)) return Problem(statusCode: 400, title: "Nhu cầu không hợp lệ");
        if (file is null || file.Length == 0) return Problem(statusCode: 400, title: "Chưa chọn video");
        if (file.Length > MaxVideoBytes) return Problem(statusCode: 400, title: "Video tối đa 12 MB");

        var contentType = (file.ContentType ?? string.Empty).ToLowerInvariant();
        var name = (file.FileName ?? string.Empty).ToLowerInvariant();
        var extension = contentType switch
        {
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "image/gif" => ".gif",
            _ => name.EndsWith(".mp4") ? ".mp4" : name.EndsWith(".webm") ? ".webm" : name.EndsWith(".gif") ? ".gif" : null,
        };
        if (extension is null) return Problem(statusCode: 400, title: "Chỉ nhận video mp4 / webm hoặc ảnh động gif");

        await using var stream = file.OpenReadStream();
        var url = await storage.SaveAsync(stream, extension, ct);

        var video = new NudgeVideo
        {
            Kind = cleanKind,
            Url = url,
            Label = (label ?? string.Empty).Trim() is { Length: > 0 } l && l.Length <= 60 ? l : null,
            UploadedById = User.GetUserId(),
        };
        db.NudgeVideos.Add(video);
        await db.SaveChangesAsync(ct);

        return new NudgeVideoDto(video.Id, video.Kind, video.Url, video.Label, video.IsActive, video.CreatedAt);
    }

    /// <summary>Bật / tắt một video (tắt = không gửi kèm nữa nhưng vẫn giữ trong thư viện).</summary>
    [HttpPost("/api/admin/nudge-videos/{id:int}/toggle")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Toggle(int id, CancellationToken ct)
    {
        var video = await db.NudgeVideos.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (video is null) return NotFound();

        video.IsActive = !video.IsActive;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("/api/admin/nudge-videos/{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var video = await db.NudgeVideos.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (video is null) return NotFound();

        db.NudgeVideos.Remove(video);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
