using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<NotificationDto>> List([FromQuery] int take = 50)
    {
        var me = User.GetUserId();
        var limit = Math.Clamp(take, 1, 100);

        return await db.Notifications
            .AsNoTracking()
            .Where(n => n.ReceiverId == me)
            .OrderByDescending(n => n.CreatedAt)
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
}
