namespace DrinkWater.Api.Storage;

public static class ImageUpload
{
    /// <summary>Kiểm tra kích thước và định dạng thật (magic bytes) trước khi lưu.
    /// Trả về phần mở rộng hợp lệ, hoặc lỗi để controller trả 400.</summary>
    public static async Task<(string? Extension, string? Error)> PrepareAsync(IFormFile file, long maxBytes)
    {
        if (file.Length == 0)
            return (null, "File rỗng");
        if (file.Length > maxBytes)
            return (null, $"Ảnh vượt quá {maxBytes / 1024 / 1024} MB");

        var head = new byte[12];
        await using (var probe = file.OpenReadStream())
        {
            var read = await probe.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false);
            if (read < head.Length) return (null, "File quá nhỏ để là ảnh");
        }

        var extension = ImageValidator.DetectExtension(head);
        return extension is null
            ? (null, "Chỉ nhận ảnh JPEG, PNG hoặc WebP")
            : (extension, null);
    }
}
