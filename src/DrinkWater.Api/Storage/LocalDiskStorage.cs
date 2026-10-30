using DrinkWater.Api.Services;

namespace DrinkWater.Api.Storage;

/// <summary>Writes under wwwroot/uploads so files are served by UseStaticFiles.
/// Swap this for Firebase/S3 later; nothing else in the app knows where bytes go.</summary>
public sealed class LocalDiskStorage(
    IWebHostEnvironment env,
    IClock clock) : IImageStorage
{
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var now = clock.LocalNow;
        var folder = Path.Combine(env.WebRootPath ?? "wwwroot", "uploads", now.ToString("yyyy"), now.ToString("MM"));
        Directory.CreateDirectory(folder);

        var name = $"{Guid.NewGuid():N}{extension}";
        await using (var file = File.Create(Path.Combine(folder, name)))
            await content.CopyToAsync(file, ct);

        return $"/uploads/{now:yyyy}/{now:MM}/{name}";
    }
}
