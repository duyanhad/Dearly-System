using System.Net.Http.Headers;
using DrinkWater.Api.Services;

namespace DrinkWater.Api.Storage;

/// <summary>Uploads to a Supabase Storage bucket over the REST API (no SDK).
/// Config: Storage:Supabase:Url, Storage:Supabase:Key (service_role), Storage:Supabase:Bucket.
/// Bucket must be public; SaveAsync returns the public object URL.</summary>
public sealed class SupabaseStorage(
    HttpClient http,
    IConfiguration config,
    IClock clock) : IImageStorage
{
    private readonly string _url = (config["Storage:Supabase:Url"] ?? "").TrimEnd('/');
    private readonly string _key = config["Storage:Supabase:Key"] ?? "";
    private readonly string _bucket = config["Storage:Supabase:Bucket"] ?? "uploads";

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var now = clock.LocalNow;
        var path = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";

        // Ảnh được nén + thu nhỏ trước khi gửi để trang tải nhanh (file âm thanh giữ nguyên).
        var payload = content;
        var storedExtension = extension;
        if (ImageOptimizer.IsImage(extension))
        {
            var (bytes, ext) = await ImageOptimizer.OptimizeAsync(content, extension, ct: ct);
            storedExtension = ext;
            path = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{ext}";
            payload = new MemoryStream(bytes);
        }
        await using var owned = payload as MemoryStream ?? null;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_url}/storage/v1/object/{_bucket}/{path}")
        {
            Content = new StreamContent(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        req.Headers.TryAddWithoutValidation("apikey", _key);
        req.Headers.TryAddWithoutValidation("x-upsert", "true");
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(Mime(storedExtension));

        var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Supabase upload thất bại ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync(ct)}");

        return $"{_url}/storage/v1/object/public/{_bucket}/{path}";
    }

    private static string Mime(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };
}
