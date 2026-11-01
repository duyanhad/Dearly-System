using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Controllers;

/// <summary>Catalog cốc mẫu dùng chung: admin thêm / xoá loại cốc kèm ảnh và dung tích.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/cups")]
public class AdminCupsController(
    AppDbContext db,
    IImageStorage storage,
    IOptions<StorageOptions> storageOptions) : ControllerBase
{
    [HttpGet]
    public async Task<List<CupDto>> List() =>
        await db.CupPresets
            .AsNoTracking()
            .Where(c => c.UserId == null)
            .OrderBy(c => c.VolumeMl)
            .Select(c => new CupDto(c.Id, c.Name, c.VolumeMl, c.ImageUrl, c.IsDefault, c.CutoutUrl, true))
            .ToListAsync();

    /// <summary>Thêm cốc mẫu (multipart: name, volumeMl, image?).</summary>
    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<CupDto>> Create(
        [FromForm] string name,
        [FromForm] int volumeMl,
        IFormFile? image)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0 || clean.Length > 60)
            return Problem(statusCode: 400, title: "Tên cốc không hợp lệ");
        if (volumeMl is < 50 or > 5000)
            return Problem(statusCode: 400, title: "Dung tích phải trong khoảng 50–5000 ml");

        string? imageUrl = null;
        if (image is { Length: > 0 })
        {
            var (extension, error) = await ImageUpload.PrepareAsync(image, storageOptions.Value.MaxBytes);
            if (error is not null) return Problem(statusCode: 400, title: error);
            await using var stream = image.OpenReadStream();
            imageUrl = await storage.SaveAsync(stream, extension!);
        }

        var cup = new CupPreset
        {
            UserId = null,
            Name = clean,
            VolumeMl = volumeMl,
            ImageUrl = imageUrl,
            IsDefault = false,
        };
        db.CupPresets.Add(cup);
        await db.SaveChangesAsync();
        return new CupDto(cup.Id, cup.Name, cup.VolumeMl, cup.ImageUrl, cup.IsDefault, cup.CutoutUrl, true);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var cup = await db.CupPresets.FirstOrDefaultAsync(c => c.Id == id && c.UserId == null);
        if (cup is null) return NotFound();
        db.CupPresets.Remove(cup);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
