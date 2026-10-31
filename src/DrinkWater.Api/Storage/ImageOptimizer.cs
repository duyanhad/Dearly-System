using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
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

    /// <summary>Thu nhỏ để cạnh dài không quá <paramref name="maxSide"/> và nén lại theo định dạng.</summary>
    public static async Task<byte[]> OptimizeAsync(Stream source, string extension, int maxSide = 1280, int quality = 82, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        try
        {
            using var image = await Image.LoadAsync(buffer, ct);
            if (image.Width > maxSide || image.Height > maxSide)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxSide, maxSide),
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            using var output = new MemoryStream();
            switch (extension.ToLowerInvariant())
            {
                case ".png":
                    await image.SaveAsPngAsync(output, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression }, ct);
                    break;
                case ".webp":
                    await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = quality }, ct);
                    break;
                default:
                    await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = quality }, ct);
                    break;
            }

            // Nén không có lợi (ảnh đã nhỏ) → giữ bản gốc.
            return output.Length < buffer.Length ? output.ToArray() : buffer.ToArray();
        }
        catch
        {
            // Ảnh lạ/không giải mã được: dùng nguyên bản, không làm hỏng luồng upload.
            return buffer.ToArray();
        }
    }
}
