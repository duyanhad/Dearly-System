using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DrinkWater.Api.Storage;

/// <summary>
/// Nén + thu nhỏ ảnh ngay khi upload để trang tải nhanh (ảnh điện thoại 3-6 MB → ~150-300 KB).
/// Ảnh PNG đã tách nền được giữ nguyên định dạng (cần độ trong suốt) nhưng vẫn thu nhỏ.
/// </summary>
public static class ImageOptimizer
{
    /// <summary>Ảnh raster cần nén trước khi lưu (âm thanh/tệp khác giữ nguyên).</summary>
    public static bool IsImage(string extension) =>
        extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";

    /// <summary>Thu nhỏ + nén ảnh; trả cả phần mở rộng mới (PNG không có alpha → JPEG cho nhẹ hơn nhiều).</summary>
    public static async Task<(byte[] Bytes, string Extension)> OptimizeAsync(
        Stream source, string extension, int maxSide = 1280, int quality = 82, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        try
        {
            using var image = await Image.LoadAsync<Rgba32>(buffer, ct);
            if (image.Width > maxSide || image.Height > maxSide)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxSide, maxSide),
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            var original = extension.ToLowerInvariant();
            // Ảnh chụp thường (không trong suốt) → JPEG: nhẹ hơn PNG 5-10 lần, trang tải nhanh hẳn.
            var target = original;
            if (original == ".png" && !HasTransparency(image)) target = ".jpg";

            using var output = new MemoryStream();
            if (target == ".png")
                await image.SaveAsPngAsync(output, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression }, ct);
            else if (target == ".webp")
                await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = quality }, ct);
            else
                await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = quality }, ct);

            // Nén không có lợi → giữ bản gốc.
            return output.Length < buffer.Length ? (output.ToArray(), target) : (buffer.ToArray(), original);
        }
        catch
        {
            // Ảnh lạ/không giải mã được: dùng nguyên bản, không làm hỏng luồng upload.
            return (buffer.ToArray(), extension);
        }
    }

    /// <summary>Kiểm tra ảnh có pixel trong suốt (quét mẫu để không tốn CPU).</summary>
    private static bool HasTransparency(Image<Rgba32> image)
    {
        var stepX = Math.Max(1, image.Width / 120);
        var stepY = Math.Max(1, image.Height / 120);
        for (var y = 0; y < image.Height; y += stepY)
        for (var x = 0; x < image.Width; x += stepX)
            if (image[x, y].A < 250) return true;
        return false;
    }
}
