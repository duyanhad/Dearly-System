using System.ComponentModel.DataAnnotations;
using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Controllers;

[ApiController]
[Route("api/push")]
public class PushController(
    AppDbContext db,
    IOptions<WebPushOptions> options,
    IWebPushSender webPush) : ControllerBase
{
    /// <summary>Khoá công khai VAPID để client đăng ký nhận thông báo.</summary>
    [HttpGet("key")]
    [AllowAnonymous]
    public ActionResult<object> Key() =>
        new { publicKey = options.Value.PublicKey, enabled = options.Value.IsConfigured };

    [HttpPost("subscribe")]
    [Authorize]
    public async Task<ActionResult<object>> Subscribe(SubscribeRequest req)
    {
        // Chỉ nhận endpoint của dịch vụ push thật (chặn SSRF vào mạng nội bộ).
        if (!PushEndpointValidator.IsAllowed(req.Endpoint))
            return Problem(statusCode: 400, title: "Endpoint push không hợp lệ");

        var me = User.GetUserId();
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                UserId = me,
                Endpoint = req.Endpoint,
                P256dh = req.P256dh,
                Auth = req.Auth
            });
        }
        else
        {
            // Cùng thiết bị đăng nhập tài khoản khác → chuyển chủ sở hữu.
            existing.UserId = me;
            existing.P256dh = req.P256dh;
            existing.Auth = req.Auth;
        }

        await db.SaveChangesAsync();
        return new { subscribed = true };
    }

    [HttpPost("unsubscribe")]
    [Authorize]
    public async Task<ActionResult<object>> Unsubscribe(SubscribeRequest req)
    {
        await db.PushSubscriptions
            .Where(s => s.Endpoint == req.Endpoint && s.UserId == User.GetUserId())
            .ExecuteDeleteAsync();
        return new { subscribed = false };
    }

    /// <summary>Gửi thử một thông báo tới các thiết bị của chính mình (kiểm chứng push đã hoạt động).</summary>
    [HttpPost("test")]
    [Authorize]
    public async Task<ActionResult<object>> Test()
    {
        var sent = await webPush.SendToUserAsync(User.GetUserId(),
            "Thử thông báo 💧", "Nếu bạn thấy dòng này cả khi đã tắt màn hình thì push đã chạy.", "normal", 9999);
        return new { sent };
    }
}

public record SubscribeRequest(
    [MaxLength(500)] string Endpoint,
    [MaxLength(200)] string P256dh,
    [MaxLength(100)] string Auth);
