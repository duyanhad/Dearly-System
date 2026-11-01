using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(
    AppDbContext db,
    IHubContext<NotificationHub> hub,
    IWebPushSender webPush) : ControllerBase
{
    /// <summary>Danh sách thông báo mới nhất. Truyền beforeId để lấy tiếp thông báo cũ hơn.</summary>
    [HttpGet]
    public async Task<List<NotificationDto>> List([FromQuery] int take = 50, [FromQuery] int beforeId = 0)
    {
        var me = User.GetUserId();
        var limit = Math.Clamp(take, 1, 100);

        var query = db.Notifications
            .AsNoTracking()
            .Where(n => n.ReceiverId == me);
        if (beforeId > 0) query = query.Where(n => n.Id < beforeId);

        return await query
            .OrderByDescending(n => n.Id)
            .Take(limit)
            .Select(n => new NotificationDto(
                n.Id, n.SenderId, n.Title, n.Message, n.ImageUrl, n.Type, n.IsRead, n.CreatedAt))
            .ToListAsync();
    }

    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var me = User.GetUserId();
        var row = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.ReceiverId == me);
        if (row is null) return NotFound();

        row.IsRead = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var me = User.GetUserId();
        await db.Notifications
            .Where(n => n.ReceiverId == me && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return NoContent();
    }

    /// <summary>Trả lời lại một thông báo (vd. "ảnh đã được duyệt") — gửi thông báo + push tới người gửi.</summary>
    [HttpPost("{id:int}/reply")]
    public async Task<ActionResult<NotificationDto>> Reply(int id, ReplyNotificationRequest req)
    {
        var me = User.GetUserId();
        var text = (req.Message ?? string.Empty).Trim();
        if (text.Length == 0) return Problem(statusCode: 400, title: "Chưa nhập nội dung trả lời");

        var source = await db.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id && n.ReceiverId == me && n.SenderId != null);
        if (source is null) return NotFound();

        var myName = await db.Users.Where(u => u.Id == me).Select(u => u.FullName).FirstAsync();
        var row = new Notification
        {
            SenderId = me,
            ReceiverId = source.SenderId,
            Title = "Phản hồi từ " + myName,
            Message = $"Về \"{source.Title}\": {text}",
            Type = NotificationTypes.Reply,
        };
        db.Notifications.Add(row);

        var sourceRow = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.ReceiverId == me);
        if (sourceRow is not null) sourceRow.IsRead = true;

        await db.SaveChangesAsync();

        if (source.SenderId is { } to)
        {
            await hub.Clients.User(to.ToString()).SendAsync("ReceiveNudge", new
            {
                Title = row.Title,
                Message = row.Message,
            });
            webPush.QueueForUser(to, row.Title, row.Message);
        }

        return new NotificationDto(row.Id, row.SenderId, row.Title, row.Message, row.ImageUrl,
            row.Type, row.IsRead, row.CreatedAt);
    }
}