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
[Authorize(Roles = Roles.Admin)]
[Route("api/admin")]
public class AdminController(
    AppDbContext db,
    SystemLogWriter logs,
    IPushSender push,
    IHubContext<NotificationHub> hub) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> Users(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? q = null)
    {
        var size = Math.Clamp(pageSize, 1, 100);
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.Contains(term) || u.FullName.ToLower().Contains(term));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * size)
            .Take(size)
            .Select(u => new AdminUserDto(
                u.Id, u.Email, u.FullName, u.Role, u.IsLocked, u.CreatedAt,
                db.WaterLogs.Count(l => l.UserId == u.Id), u.AvatarUrl))
            .ToListAsync();

        return new PagedResult<AdminUserDto>(items, Math.Max(1, page), size, total);
    }

    [HttpPut("users/{userId:guid}/status")]
    public async Task<ActionResult<AdminUserDto>> SetStatus(Guid userId, UpdateUserStatusRequest req)
    {
        var me = User.GetUserId();
        if (userId == me && req.IsLocked)
            return Problem(statusCode: 400, title: "Không thể tự khóa tài khoản của mình");

        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        user.IsLocked = req.IsLocked;
        // Khoá/mở khoá phải có hiệu lực ngay: đổi stamp để token cũ không dùng được nữa.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();

        await logs.WriteAsync(me, req.IsLocked ? "LockUser" : "UnlockUser", $"user={user.Email}");

        return ToDto(user);
    }

    [HttpPut("users/{userId:guid}/role")]
    public async Task<ActionResult<AdminUserDto>> SetRole(Guid userId, UpdateUserRoleRequest req)
    {
        if (req.Role is not (Roles.User or Roles.Admin))
            return Problem(statusCode: 400, title: "Role chỉ nhận User hoặc Admin");

        var me = User.GetUserId();
        if (userId == me)
            return Problem(statusCode: 400, title: "Không thể tự đổi role của mình");

        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        user.Role = req.Role;
        // Đổi quyền phải có hiệu lực ngay với token đang dùng.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();

        await logs.WriteAsync(me, "ChangeRole", $"user={user.Email}, role={req.Role}");

        return ToDto(user);
    }

    /// <summary>Admin đặt lại mật khẩu một tài khoản. Để trống NewPassword thì server sinh mật khẩu ngẫu nhiên.</summary>
    [HttpPut("users/{userId:guid}/password")]
    public async Task<ActionResult<AdminResetPasswordResult>> ResetPassword(
        Guid userId, AdminResetPasswordRequest req)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var password = string.IsNullOrWhiteSpace(req.NewPassword)
            ? GeneratePassword()
            : req.NewPassword.Trim();

        if (password.Length < 8)
            return Problem(statusCode: 400, title: "Mật khẩu mới phải dài tối thiểu 8 ký tự");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        // Mật khẩu mới phải có hiệu lực ngay: vô hiệu hoá mọi token đã phát.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();

        await logs.WriteAsync(User.GetUserId(), "ResetPassword", $"user={user.Email}");

        return new AdminResetPasswordResult(user.Email, password);
    }

    /// <summary>Mật khẩu tạm 10 ký tự, bỏ các ký tự dễ nhầm (0/O, 1/l/I).</summary>
    private static string GeneratePassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var bytes = new byte[10];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return string.Concat(bytes.Select(b => alphabet[b % alphabet.Length]));
    }

    [HttpPost("broadcast")]
    public async Task<ActionResult<NotificationDto>> Broadcast(BroadcastRequest req)    {
        var me = User.GetUserId();
        var receivers = await db.Users
            .Where(u => !u.IsLocked)
            .Select(u => new { u.Id, u.FCMToken })
            .ToListAsync();

        var notification = new Notification
        {
            SenderId = me,
            Title = req.Title.Trim(),
            Message = req.Message.Trim(),
            Type = NotificationTypes.Broadcast
        };

        // One row per recipient so each user's notification center is independent.
        var rows = receivers.Select(r => new Notification
        {
            SenderId = me,
            ReceiverId = r.Id,
            Title = notification.Title,
            Message = notification.Message,
            Type = NotificationTypes.Broadcast
        }).ToList();

        db.Notifications.AddRange(rows);
        await db.SaveChangesAsync();

        await hub.Clients.All.SendAsync("ReceiveBroadcast", new
        {
            Title = notification.Title,
            Message = notification.Message,
            CreatedAt = notification.CreatedAt
        });

        foreach (var r in receivers)
            await push.SendAsync(r.FCMToken, notification.Title, notification.Message);

        await logs.WriteAsync(me, "Broadcast", $"receivers={receivers.Count}, title={notification.Title}");

        return new NotificationDto(rows[0].Id, me, notification.Title, notification.Message,
            null, notification.Type, false, notification.CreatedAt);
    }

    [HttpGet("logs")]
    public async Task<ActionResult<PagedResult<SystemLogDto>>> GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? level = null)
    {
        var size = Math.Clamp(pageSize, 1, 200);
        var query = db.SystemLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(l => l.LogLevel == level);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * size)
            .Take(size)
            .Select(l => new SystemLogDto(l.Id, l.UserId, l.Action, l.Details, l.LogLevel, l.CreatedAt))
            .ToListAsync();

        return new PagedResult<SystemLogDto>(items, Math.Max(1, page), size, total);
    }

    private static AdminUserDto ToDto(User u) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.IsLocked, u.CreatedAt, 0, u.AvatarUrl);
}

