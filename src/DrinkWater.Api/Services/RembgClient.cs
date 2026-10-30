using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Services;

/// <summary>
/// Gọi dịch vụ tách nền rembg (Python sidecar: <c>rembg s --host 127.0.0.1 --port 7000</c>).
/// Trả về PNG đã trong suốt, hoặc null nếu dịch vụ tắt/không chạy/lỗi (khi đó client tự tách).
/// </summary>
public sealed class RembgClient(HttpClient http, IOptions<RembgOptions> options, ILogger<RembgClient> logger)
{
    public async Task<byte[]?> RemoveBackgroundAsync(byte[] image, string fileName, CancellationToken ct = default)
    {
        var opt = options.Value;
        if (!opt.Enabled) return null;

        try
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(image);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
            if (!string.IsNullOrWhiteSpace(opt.Model))
                form.Add(new StringContent(opt.Model), "model");
            if (opt.DecontaminateEdges)
                form.Add(new StringContent("true"), "dc");

            using var res = await http.PostAsync($"{opt.Url.TrimEnd('/')}/api/remove", form, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("rembg trả {Status} cho {File}", (int)res.StatusCode, fileName);
                return null;
            }

            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length > 100 ? bytes : null;
        }
        catch (Exception ex)
        {
            // Dịch vụ tách nền là tuỳ chọn: không được làm hỏng việc upload ảnh cốc.
            logger.LogWarning(ex, "Không gọi được rembg ({Url})", opt.Url);
            return null;
        }
    }
}
