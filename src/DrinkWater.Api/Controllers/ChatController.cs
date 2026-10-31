using System.ComponentModel.DataAnnotations;
using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using DrinkWater.Api.Services;
using DrinkWater.Api.Storage;
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
    IHubContext<NotificationHub> hub,
    IWebPushSender webPush,
    IImageStorage storage) : ControllerBase
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

        // Cảm xúc trên từng tin (nhóm một lần để không truy vấn lặp).
        var ids = messages.Select(m => m.Id).ToList();
        var reactions = await db.MessageReactions
            .AsNoTracking()
            .Where(r => ids.Contains(r.MessageId))
            .ToListAsync(ct);

        List<ReactionDto> ReactionsOf(int messageId) => reactions
            .Where(r => r.MessageId == messageId)
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionDto(g.Key, g.Count(), g.Any(r => r.UserId == me)))
            .OrderByDescending(r => r.Count)
            .ToList();

        return messages
            .Select(m => new MessageDto(m.Id, m.SenderId == me, m.Body, m.CreatedAt, m.ReadAt,
                m.AudioUrl, m.AudioDurationMs, m.ImageUrl, ReactionsOf(m.Id)))
            .ToList();
    }

    /// <summary>Gửi tin nhắn thoại (ghi âm trên máy) tới một người bạn.</summary>
    [HttpPost("{userId:guid}/voice")]
    [RequestFormLimits(MultipartBodyLengthLimit = 8 * 1024 * 1024)]
    public async Task<ActionResult<MessageDto>> SendVoice(
        Guid userId, IFormFile audio, [FromForm] int durationMs, CancellationToken ct)
    {
        var me = User.GetUserId();
        if (userId == me) return Problem(statusCode: 400, title: "Không thể tự nhắn chính mình");
        if (!await AreFriendsAsync(me, userId, ct))
            return Problem(statusCode: 403, title: "Chỉ chat được với bạn bè");
        if (audio is null || audio.Length == 0) return Problem(statusCode: 400, title: "Thiếu file ghi âm");
        if (audio.Length > MaxVoiceBytes) return Problem(statusCode: 400, title: "Ghi âm tối đa 5 MB");

        var extension = VoiceExtension(audio.ContentType);
        if (extension is null) return Problem(statusCode: 400, title: "Định dạng ghi âm không hỗ trợ");

        await using var stream = audio.OpenReadStream();
        var url = await storage.SaveAsync(stream, extension, ct);

        var message = new Message
        {
            SenderId = me,
            ReceiverId = userId,
            Body = string.Empty,
            AudioUrl = url,
            AudioDurationMs = Math.Clamp(durationMs, 0, 120_000)
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
            message.AudioUrl,
            message.AudioDurationMs,
            message.ImageUrl,
            message.CreatedAt
        }, ct);

        // Push chạy nền để phản hồi gửi tin không phải chờ dịch vụ push.
        _ = webPush.SendToUserAsync(userId, sender.FullName ?? "Tin nhắn mới", "Đã gửi một tin nhắn thoại 🎤",
            "normal");

        return new MessageDto(message.Id, true, message.Body, message.CreatedAt, null,
            message.AudioUrl, message.AudioDurationMs, message.ImageUrl, []);
    }

    private const long MaxVoiceBytes = 5 * 1024 * 1024;

    /// <summary>Chỉ nhận định dạng ghi âm phổ biến của trình duyệt (iOS ghi mp4, Chrome ghi webm/ogg).</summary>
    private static string? VoiceExtension(string? contentType) => contentType?.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "audio/mp4" or "audio/m4a" or "audio/x-m4a" => ".m4a",
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        "audio/mpeg" => ".mp3",
        "audio/wav" or "audio/x-wav" => ".wav",
        _ => null
    };

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
            message.ImageUrl,
            message.CreatedAt
        }, ct);

        // Web Push để người nhận thấy tin nhắn cả khi đã đóng app / tắt màn hình (chạy nền cho nhanh).
        _ = webPush.SendToUserAsync(userId, sender.FullName ?? "Tin nhắn mới",
            body.Length > 120 ? body[..120] + "…" : body, "normal");

        return new MessageDto(message.Id, true, message.Body, message.CreatedAt, null,
            null, null, message.ImageUrl, []);
    }

    /// <summary>Gửi ảnh trong chat (multipart: image, tối đa 5 MB).</summary>
    [HttpPost("{userId:guid}/image")]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<MessageDto>> SendImage(Guid userId, IFormFile image, CancellationToken ct)
    {
        var me = User.GetUserId();
        if (userId == me) return Problem(statusCode: 400, title: "Không thể tự nhắn chính mình");
        if (!await AreFriendsAsync(me, userId, ct))
            return Problem(statusCode: 403, title: "Chỉ chat được với bạn bè");
        if (image is null || image.Length == 0) return Problem(statusCode: 400, title: "Thiếu ảnh");
        if (image.Length > MaxVoiceBytes) return Problem(statusCode: 400, title: "Ảnh tối đa 5 MB");

        var (extension, error) = await ImageUpload.PrepareAsync(image, MaxVoiceBytes);
        if (error is not null) return Problem(statusCode: 400, title: error);
        await using var stream = image.OpenReadStream();
        var url = await storage.SaveAsync(stream, extension!, ct);

        var message = new Message { SenderId = me, ReceiverId = userId, Body = string.Empty, ImageUrl = url };
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
            message.ImageUrl,
            message.CreatedAt
        }, ct);

        _ = webPush.SendToUserAsync(userId, sender.FullName ?? "Tin nhắn mới", "Đã gửi một bức ảnh 🖼️", "normal");

        return new MessageDto(message.Id, true, message.Body, message.CreatedAt, null,
            null, null, message.ImageUrl, []);
    }

    /// <summary>Thả / đổi / bỏ cảm xúc trên một tin nhắn (chỉ hai người trong hội thoại).</summary>
    [HttpPost("messages/{messageId:int}/react")]
    public async Task<ActionResult<List<ReactionDto>>> React(int messageId, ReactRequest req, CancellationToken ct)
    {
        var me = User.GetUserId();
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message is null) return NotFound();
        if (message.SenderId != me && message.ReceiverId != me)
            return Problem(statusCode: 403, title: "Không phải tin nhắn của bạn");

        var emoji = (req.Emoji ?? string.Empty).Trim();
        var mine = await db.MessageReactions.FirstOrDefaultAsync(r => r.MessageId == messageId && r.UserId == me, ct);
        if (mine is not null && mine.Emoji == emoji)
            db.MessageReactions.Remove(mine); // bấm lại đúng emoji đó → bỏ cảm xúc
        else if (mine is not null)
            mine.Emoji = emoji;
        else if (emoji.Length > 0)
            db.MessageReactions.Add(new MessageReaction { MessageId = messageId, UserId = me, Emoji = emoji });
        await db.SaveChangesAsync(ct);

        // GroupBy dịch sang SQL không hỗ trợ Any() → kéo về rồi nhóm trong bộ nhớ.
        var rows = await db.MessageReactions.AsNoTracking()
            .Where(r => r.MessageId == messageId)
            .ToListAsync(ct);
        var reactions = rows
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionDto(g.Key, g.Count(), g.Any(r => r.UserId == me)))
            .OrderByDescending(r => r.Count)
            .ToList();

        var other = message.SenderId == me ? message.ReceiverId : message.SenderId;
        await hub.Clients.User(other.ToString()).SendAsync("ReceiveReaction", new { MessageId = messageId, Reactions = reactions }, ct);
        return reactions;
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

public record MessageDto(int Id, bool Mine, string Body, DateTime CreatedAt, DateTime? ReadAt,
    string? AudioUrl = null, int? AudioDurationMs = null, string? ImageUrl = null,
    List<ReactionDto>? Reactions = null);

/// <summary>Một cảm xúc trên tin nhắn: emoji, số người thả và tôi đã thả chưa.</summary>
public record ReactionDto(string Emoji, int Count, bool Mine);

public record ReactRequest([MaxLength(16)] string Emoji);
