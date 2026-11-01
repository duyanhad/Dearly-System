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

public record CupRequest(
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(50)] string Name,
    [System.ComponentModel.DataAnnotations.Range(1, 5000)] int VolumeMl);

public record CupDto(int Id, string Name, int VolumeMl, string? ImageUrl, bool IsDefault, string? CutoutUrl = null, bool IsCatalog = false);

[ApiController]
[Authorize]
[Route("api/cups")]
public class CupsController(
    AppDbContext db,
    IImageStorage storage,
    RembgClient rembg,
    IOptions<StorageOptions> storageOptions,
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public async Task<List<CupDto>> List()
    {
        var me = User.GetUserId();
        var cups = await db.CupPresets
            .Where(c => c.UserId == me || c.UserId == null)
            .OrderByDescending(c => c.UserId == me)
            .ThenByDescending(c => c.IsDefault)
            .ThenBy(c => c.VolumeMl)
            .Select(c => new CupDto(c.Id, c.Name, c.VolumeMl, c.ImageUrl, c.IsDefault, c.CutoutUrl, c.UserId == null))
            .ToListAsync();

        // Ảnh cũ chưa có bản tách nền → nhờ rembg chạy nền, lần tải sau client nhận PNG sẵn
        // (tránh để trình duyệt phải tải model AI, rất chậm).
        if (cups.Any(c => c.ImageUrl is not null && c.CutoutUrl is null) && BackfillGuard.TryAdd(me, 0))
            _ = Task.Run(async () =>
            {
                try { await BackfillCutoutsAsync(me); }
                finally { BackfillGuard.TryRemove(me, out _); }
            });

        return cups;
    }

    /// <summary>Chọn cốc mẫu từ catalog: sao chép thành cốc của người dùng và đặt mặc định.</summary>
    [HttpPost("{id:int}/adopt")]
    public async Task<ActionResult<CupDto>> Adopt(int id)
    {
        var me = User.GetUserId();
        var cup = await db.CupPresets.FirstOrDefaultAsync(c => c.Id == id);
        if (cup is null) return NotFound();

        if (cup.UserId == me)
        {
            await db.CupPresets.Where(c => c.UserId == me && c.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false));
            cup.IsDefault = true;
            await db.SaveChangesAsync();
            return new CupDto(cup.Id, cup.Name, cup.VolumeMl, cup.ImageUrl, cup.IsDefault, cup.CutoutUrl, false);
        }
        if (cup.UserId is not null) return Problem(statusCode: 403, title: "Cốc của người khác");

        await db.CupPresets.Where(c => c.UserId == me && c.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsDefault, false));
        var mine = new CupPreset
        {
            UserId = me,
            Name = cup.Name,
            VolumeMl = cup.VolumeMl,
            ImageUrl = cup.ImageUrl,
            CutoutUrl = cup.CutoutUrl,
            IsDefault = true,
        };
        db.CupPresets.Add(mine);
        await db.SaveChangesAsync();
        return new CupDto(mine.Id, mine.Name, mine.VolumeMl, mine.ImageUrl, mine.IsDefault, mine.CutoutUrl, false);
    }

    // Mỗi user chỉ chạy một lượt tách nền, tránh gọi rembg trùng lặp khi client tải lại nhiều lần.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> BackfillGuard = new();

    /// <summary>Tách nền các ảnh cốc cũ (chạy nền, không chặn request).</summary>
    private async Task BackfillCutoutsAsync(Guid userId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbc = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rb = scope.ServiceProvider.GetRequiredService<RembgClient>();
            var st = scope.ServiceProvider.GetRequiredService<IImageStorage>();
            var host = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

            var pending = await dbc.CupPresets
                .Where(c => c.UserId == userId && c.ImageUrl != null && c.CutoutUrl == null)
                .ToListAsync();

            foreach (var cup in pending)
            {
                var relative = cup.ImageUrl!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var path = Path.Combine(host.WebRootPath, relative);
                if (!System.IO.File.Exists(path)) continue;

                var bytes = await System.IO.File.ReadAllBytesAsync(path);
                var cut = await rb.RemoveBackgroundAsync(bytes, Path.GetFileName(path));
                if (cut is null) continue;

                using var ms = new MemoryStream(cut);
                cup.CutoutUrl = await st.SaveAsync(ms, ".png");
                await dbc.SaveChangesAsync();
            }
        }
        catch
        {
            // Chạy nền: lỗi thì để lần sau thử lại, không ảnh hưởng người dùng.
        }
    }

    [HttpPost]
    public async Task<ActionResult<CupDto>> Create(CupRequest req)
    {
        var me = User.GetUserId();
        var cup = new CupPreset
        {
            UserId = me,
            Name = req.Name.Trim(),
            VolumeMl = req.VolumeMl,
            IsDefault = !await db.CupPresets.AnyAsync(c => c.UserId == me)
        };
        db.CupPresets.Add(cup);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new CupDto(cup.Id, cup.Name, cup.VolumeMl, cup.ImageUrl, cup.IsDefault, cup.CutoutUrl));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CupDto>> Update(int id, CupRequest req)
    {
        var cup = await Find(id);
        if (cup is null) return NotFound();

        cup.Name = req.Name.Trim();
        cup.VolumeMl = req.VolumeMl;
        await db.SaveChangesAsync();

        return new CupDto(cup.Id, cup.Name, cup.VolumeMl, cup.ImageUrl, cup.IsDefault, cup.CutoutUrl);
    }

    /// <summary>?nh c?c d�ng l�m texture cho ly 3D.</summary>
    [HttpPut("{id:int}/image")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<CupDto>> UploadImage(int id, IFormFile image)
    {
        var cup = await Find(id);
        if (cup is null) return NotFound();

        var (extension, error) = await ImageUpload.PrepareAsync(image, storageOptions.Value.MaxBytes);
        if (error is not null) return Problem(statusCode: 400, title: error);

        await using (var stream = image.OpenReadStream())
            cup.ImageUrl = await storage.SaveAsync(stream, extension!);

        // Tách nền ở server bằng rembg (nếu có) để cốc 3D dùng PNG trong suốt sẵn —
        // người dùng không phải tải model AI về máy.
        cup.CutoutUrl = null;
        await db.SaveChangesAsync();

        using (var buffer = new MemoryStream())
        {
            await using (var source = image.OpenReadStream())
                await source.CopyToAsync(buffer);

            var cut = await rembg.RemoveBackgroundAsync(buffer.ToArray(), image.FileName, HttpContext.RequestAborted);
            if (cut is not null)
            {
                using var cutStream = new MemoryStream(cut);
                cup.CutoutUrl = await storage.SaveAsync(cutStream, ".png");
                await db.SaveChangesAsync();
            }
        }

        return new CupDto(cup.Id, cup.Name, cup.VolumeMl, cup.ImageUrl, cup.IsDefault, cup.CutoutUrl);
    }

    [HttpDelete("{id:int}/image")]
    public async Task<ActionResult<CupDto>> RemoveImage(int id)
    {
        var cup = await Find(id);
        if (cup is null) return NotFound();

        cup.ImageUrl = null;
        cup.CutoutUrl = null;
        await db.SaveChangesAsync();
        return new CupDto(cup.Id, cup.Name, cup.VolumeMl, cup.ImageUrl, cup.IsDefault, cup.CutoutUrl);
    }

    /// <summary>Chọn cốc hiển thị trên dashboard 3D.</summary>
    [HttpPut("{id:int}/default")]
    public async Task<ActionResult<CupDto>> SetDefault(int id)
    {
        var me = User.GetUserId();
        var cup = await Find(id);
        if (cup is null) return NotFound();

        var all = await db.CupPresets.Where(c => c.UserId == me).ToListAsync();
        foreach (var item in all)
            item.IsDefault = item.Id == cup.Id;

        await db.SaveChangesAsync();
        return new CupDto(cup.Id, cup.Name, cup.VolumeMl, cup.ImageUrl, cup.IsDefault, cup.CutoutUrl);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var cup = await Find(id);
        if (cup is null) return NotFound();

        db.CupPresets.Remove(cup);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Scoped by UserId so another user's cup is reported as missing, not forbidden.
    private Task<CupPreset?> Find(int id) =>
        db.CupPresets.FirstOrDefaultAsync(c => c.Id == id && c.UserId == User.GetUserId());
}