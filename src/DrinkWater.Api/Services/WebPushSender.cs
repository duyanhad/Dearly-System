using System.Net;
using System.Text.Json;
using DrinkWater.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebPush;

namespace DrinkWater.Api.Services;

public interface IWebPushSender
{
    /// <summary>Gửi tới mọi thiết bị đã đăng ký của người dùng. Trả về số thiết bị đã gửi được.</summary>
    Task<int> SendToUserAsync(
        Guid userId, string title, string body, string? style = null, int? tagId = null, CancellationToken ct = default);
}

/// <summary>
/// Web Push thật (VAPID): hoạt động cả khi tab/app đã đóng, kể cả PWA trên iOS 16.4+.
/// Thiết bị đã huỷ đăng ký (404/410) thì xoá khỏi DB.
/// shortcut: gửi tuần tự từng thiết bị; đủ cho quy mô hiện tại, đổi sang hàng đợi nếu đông người dùng.
/// </summary>
public sealed class WebPushSender(
    AppDbContext db,
    IOptions<WebPushOptions> options,
    ILogger<WebPushSender> log) : IWebPushSender
{
    private readonly WebPushOptions _options = options.Value;

    public async Task<int> SendToUserAsync(
        Guid userId, string title, string body, string? style = null, int? tagId = null, CancellationToken ct = default)
    {
        if (!_options.IsConfigured) return 0;

        var subscriptions = await db.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);
        if (subscriptions.Count == 0) return 0;

        var client = new WebPushClient();
        var payload = JsonSerializer.Serialize(new
        {
            title,
            body,
            style = style ?? "normal",
            tag = tagId is null ? $"drinkwater-reminder-{userId}" : $"drinkwater-reminder-{userId}-{tagId}",
            url = "/"
        });

        var sent = 0;
        foreach (var subscription in subscriptions)
        {
            // Kiểm tra lại lúc gửi: dữ liệu cũ (trước khi có validate) có thể chứa URL nội bộ.
            if (!PushEndpointValidator.IsAllowed(subscription.Endpoint))
            {
                db.PushSubscriptions.Remove(subscription);
                log.LogWarning("Endpoint push không hợp lệ, xoá khỏi DB: {User}", userId);
                continue;
            }

            var target = new WebPush.PushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
            var vapid = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
            try
            {
                await client.SendNotificationAsync(target, payload, vapid, ct);
                sent++;
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                log.LogWarning("Thiết bị đã huỷ đăng ký push ({Status}), xoá khỏi DB: {User} — {Body}",
                    (int)ex.StatusCode, userId, ex.Message);
                db.PushSubscriptions.Remove(subscription);
            }
            catch (WebPushException ex)
            {
                log.LogWarning("Web push lỗi {Status}: {Message}", (int)ex.StatusCode, ex.Message);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Không gửi được web push cho {User}", userId);
            }
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return sent;
    }
}
