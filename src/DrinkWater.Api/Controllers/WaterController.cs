using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using DrinkWater.Api.Hubs;
using DrinkWater.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/water")]
public class WaterController(
    WaterService water,
    IImageStorage storage,
    IOptions<StorageOptions> storageOptions,
    IHubContext<NotificationHub> hub,
    AppDbContext db,
    IPushSender push,
    IWebPushSender webPush) : ControllerBase
{
    [HttpPost("log")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<WaterLogDto>> Log(
        [FromForm] AddWaterLogRequest req,
        IFormFile? image)
    {
        if (req.Period is not null && !Periods.All.Contains(req.Period))
            return Problem(statusCode: 400, title: "Ca không hợp lệ", detail: "Chỉ nhận Morning, Afternoon, Evening");

        if (image is null && await water.RequiresPhotoAsync(User.GetUserId()))
            return Problem(statusCode: 400, title: "Bắt buộc kèm ảnh cốc nước",
                detail: "Bạn đang bật \"Bắt buộc chụp ảnh\" trong Cài đặt. Hãy chụp ảnh cốc nước rồi thử lại.");

        string? imageUrl = null;
        if (image is not null)
        {
            var (extension, error) = await ImageUpload.PrepareAsync(image, storageOptions.Value.MaxBytes);
            if (error is not null) return Problem(statusCode: 400, title: error);

            await using var stream = image.OpenReadStream();
            imageUrl = await storage.SaveAsync(stream, extension!);
        }

        var log = await water.AddLogAsync(
            User.GetUserId(), req.AmountMl, req.Period, imageUrl, req.Note?.Trim());

        // Phiếu chờ duyệt → báo ngay cho partner để họ thấy mà không phải chờ tải lại trang.
        if (log.Status == LogStatuses.Pending)
        {
            var me = User.GetUserId();
            var info = await db.Users.AsNoTracking()
                .Where(u => u.Id == me)
                .Select(u => new { u.FullName, u.PartnerId })
                .FirstAsync();
            if (info.PartnerId is { } partnerId)
            {
                var payload = new
                {
                    Title = "Phiếu mới chờ duyệt",
                    Message = $"{info.FullName} vừa thêm {log.AmountMl} ml.",
                    AmountMl = log.AmountMl,
                    UserId = me
                };
                await hub.Clients.User(partnerId.ToString()).SendAsync("ReceiveProof", payload);
                // Vào chuông thông báo + web push để partner thấy cả khi đã tắt app.
                db.Notifications.Add(new Notification
                {
                    SenderId = me,
                    ReceiverId = partnerId,
                    Title = payload.Title,
                    Message = payload.Message,
                    Type = NotificationTypes.Proof,
                });
                await db.SaveChangesAsync();
                var partnerToken = await db.Users.AsNoTracking()
                    .Where(u => u.Id == partnerId)
                    .Select(u => u.FCMToken)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(partnerToken))
                    await push.SendAsync(partnerToken, payload.Title, payload.Message);
                await webPush.SendToUserAsync(partnerId, payload.Title, payload.Message, "normal", ct: HttpContext.RequestAborted);
            }
        }

        return new WaterLogDto(log.Id, log.AmountMl, log.Period, log.ImageUrl, log.Note, log.Status, log.LogTime);
    }

    [HttpGet("today")]
    public Task<TodayDto> Today() => water.GetTodayAsync(User.GetUserId());

    [HttpGet("day/{date}")]
    public async Task<ActionResult<DayDto>> Day(string date)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed))
            return Problem(statusCode: 400, title: "Ngày phải có dạng yyyy-MM-dd");

        return await water.GetDayAsync(User.GetUserId(), parsed);
    }

    [HttpGet("calendar")]
    public async Task<ActionResult<CalendarDto>> Calendar([FromQuery] int? year, [FromQuery] int? month)
    {
        var now = DateTime.UtcNow;
        var y = year ?? now.Year;
        var m = month ?? now.Month;
        if (m is < 1 or > 12)
            return Problem(statusCode: 400, title: "Tháng phải từ 1 đến 12");

        return await water.GetCalendarAsync(User.GetUserId(), y, m);
    }
}
