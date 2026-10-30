using System.ComponentModel.DataAnnotations;
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

/// <summary>Chat 1-1 giữa bạn bè (dùng chung quan hệ follow đã chấp nhận).</summary>
[ApiController]
[Authorize]
[Route("api/chat")]
public class ChatController(
    AppDbContext db,
    IClock clock,
    IHubContext<NotificationHub> hub) : ControllerBase
{
    private const int MaxBodyLength = 500;

    private async Task<bool> AreFriendsAsync(Guid me, Guid other, CancellationToken ct) =>
        await db.Follows.AnyAsync(f =>
            f.Status == FollowStatuses.Accepted &&
            ((f.FollowerId == me && f.FollowingId == other) ||
             (f.FollowerId == other && f.FollowingId == me)), ct);

    /// <summary>Danh sách bạn bè kèm tin nhắn cuối và số tin chưa đọc.</summary>
    [HttpGet("conversations")]
    public async Task<ActionResult<List<ConversationDto>>> Conversations(CancellationToken ct)
    {
        var me = User.GetUserId();
        var friendIds = await db.Follows
            .AsNoTracking()
            .Where(f => f.Status == FollowStatuses.Accepted && (f.FollowerId == me || f.FollowingId == me))
            .Select(f => f.FollowerId == me ? f.FollowingId : f.FollowerId)
            .Distinct()
            .ToListAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .Where(u => friendIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.AvatarUrl })
            .ToListAsync(ct);

        var nicknames = await db.FriendNicknames
            .AsNoTracking()
            .Where(n => n.OwnerId == me)
            .ToDictionaryAsync(n => n.TargetId, n => n.Nickname, ct);

        var lastMessages = await db.Messages
            .AsNoTracking()
            .Where(m => m.SenderId == me || m.ReceiverId == me)
            .GroupBy(m => m.SenderId == me ? m.ReceiverId : m.SenderId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).First())
            .ToListAsync(ct);

        var unread = await db.Messages
            .AsNoTracking()
            .Where(m => m.ReceiverId == me && m.ReadAt == null)
            .GroupBy(m => m.SenderId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var conversations = users
            .Select(u =>
            {
                var last = lastMessages.FirstOrDefault(m => m.SenderId == u.Id || m.ReceiverId == u.Id);
                return new ConversationDto(
                    u.Id,
                    nicknames.GetValueOrDefault(u.Id) ?? u.FullName,
                    u.AvatarUrl,
                    last?.Body,
                    last?.CreatedAt,
                    last is not null && last.SenderId == me,
                    unread.GetValueOrDefault(u.Id));
            })
            .OrderByDescending(c => c.LastAt ?? DateTime.MinValue)
            .ToList();

        return conversations;
    }

    /// <summary>Tin nhắn với một người bạn (mới nhất ở cuối) + đánh dấu đã đọc.</summary>
    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<List<MessageDto>>> Thread(Guid userId, CancellationToken ct)
    {
        var me = User.GetUserId();
        if (!await AreFriendsAsync(me, userId, ct))
            return Problem(statusCode: 403, title: "Chỉ chat được với bạn bè");

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => (m.SenderId == me && m.ReceiverId == userId) ||
                        (m.SenderId == userId && m.ReceiverId == me))
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        await db.Messages
            .Where(m => m.SenderId == userId && m.ReceiverId == me && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, clock.UtcNow), ct);

        messages.Reverse();
        return messages
            .Select(m => new MessageDto(m.Id, m.SenderId == me, m.Body, m.CreatedAt, m.ReadAt))
            .ToList();
    }

    [HttpPost("{userId:guid}")]
    public async Task<ActionResult<MessageDto>> Send(Guid userId, SendMessageRequest req, CancellationToken ct)
    {
        var me = User.GetUserId();
        if (userId == me) return Problem(statusCode: 400, title: "Không thể tự nhắn chính mình");
        if (!await AreFriendsAsync(me, userId, ct))
            return Problem(statusCode: 403, title: "Chỉ chat được với bạn bè");

        var body = (req.Body ?? string.Empty).Trim();
        if (body.Length == 0) return Problem(statusCode: 400, title: "Tin nhắn trống");
        if (body.Length > MaxBodyLength)
            return Problem(statusCode: 400, title: $"Tin nhắn tối đa {MaxBodyLength} ký tự");

        var message = new Message
        {
            SenderId = me,
            ReceiverId = userId,
            Body = body
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        var sender = await db.Users.AsNoTracking()
            .Where(u => u.Id == me)
            .Select(u => new { u.FullName, u.AvatarUrl })
            .FirstAsync(ct);

        await hub.Clients.User(userId.ToString()).SendAsync("ReceiveMessage", new
        {
            message.Id,
            FromUserId = me,
            FromName = sender.FullName,
            sender.AvatarUrl,
            message.Body,
            message.CreatedAt
        }, ct);

        return new MessageDto(message.Id, true, message.Body, message.CreatedAt, null);
    }

    /// <summary>Đánh dấu đã đọc toàn bộ tin của một người (dùng khi mở trang chat trên thiết bị khác).</summary>
    [HttpPost("{userId:guid}/read")]
    public async Task<ActionResult<object>> MarkRead(Guid userId, CancellationToken ct)
    {
        var me = User.GetUserId();
        await db.Messages
            .Where(m => m.SenderId == userId && m.ReceiverId == me && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, clock.UtcNow), ct);
        return new { read = true };
    }

    /// <summary>Xoá toàn bộ hội thoại 1-1 (cả hai phía) — quyền xoá dữ liệu của người dùng.</summary>
    [HttpDelete("{userId:guid}")]
    public async Task<ActionResult<object>> DeleteThread(Guid userId, CancellationToken ct)
    {
        var me = User.GetUserId();
        var removed = await db.Messages
            .Where(m => (m.SenderId == me && m.ReceiverId == userId) ||
                        (m.SenderId == userId && m.ReceiverId == me))
            .ExecuteDeleteAsync(ct);
        return new { deleted = removed };
    }
}

public record SendMessageRequest([MaxLength(500)] string Body);

public record ConversationDto(
    Guid UserId,
    string Name,
    string? AvatarUrl,
    string? LastBody,
    DateTime? LastAt,
    bool LastFromMe,
    int Unread);

public record MessageDto(int Id, bool Mine, string Body, DateTime CreatedAt, DateTime? ReadAt);
