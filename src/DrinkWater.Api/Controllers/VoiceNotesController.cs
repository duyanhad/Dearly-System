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
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Controllers;

/// <summary>
/// Lời nhắn thoại hẹn giờ: ghi âm → chọn người nhận + giờ gửi → tới giờ hệ thống kêu lặp
/// (VoiceNoteRinger) cho tới khi người nhận bấm "đã nghe".
/// </summary>
[ApiController]
[Authorize]
[Route("api/voice-notes")]
public class VoiceNotesController(
    AppDbContext db,
    IImageStorage storage,
    IOptions<StorageOptions> storageOptions,
    IHubContext<NotificationHub> hub,
    IClock clock) : ControllerBase
{
    private const int MaxVoiceBytes = 5 * 1024 * 1024;

    private static string? VoiceExtension(string? contentType) => contentType?.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "audio/mp4" or "audio/m4a" or "audio/x-m4a" => ".m4a",
        "audio/webm" => ".webm",
        "audio/ogg" => ".ogg",
        "audio/mpeg" => ".mp3",
        "audio/wav" or "audio/x-wav" => ".wav",
        _ => null,
    };

    private static bool IsAllowedContentType(string? contentType) => VoiceExtension(contentType) is not null;

    /// <summary>Tạo lời nhắn thoại hẹn giờ gửi cho một người bạn/partner.</summary>
    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<ActionResult<VoiceNoteDto>> Create(
        [FromForm] Guid receiverId,
        [FromForm] DateTime scheduledAtUtc,
        [FromForm] int durationMs,
        IFormFile? audio,
        [FromForm] int? replyToId,
        CancellationToken ct)
    {
        var me = User.GetUserId();
        if (receiverId == me) return Problem(statusCode: 400, title: "Không thể tự gửi cho chính mình");
        if (audio is null || audio.Length == 0) return Problem(statusCode: 400, title: "Thiếu file ghi âm");
        if (audio.Length > MaxVoiceBytes) return Problem(statusCode: 400, title: "Ghi âm tối đa 5 MB");
        if (!IsAllowedContentType(audio.ContentType)) return Problem(statusCode: 400, title: "Định dạng ghi âm không hỗ trợ");

        // Bạn bè (một trong hai chiều đã chấp nhận) hoặc partner của nhau.
        var isFriend = await db.Follows.AnyAsync(f =>
            f.Status == FollowStatuses.Accepted &&
            ((f.FollowerId == me && f.FollowingId == receiverId) ||
             (f.FollowerId == receiverId && f.FollowingId == me)), ct);
        var isPartner = await db.Users.AnyAsync(u => u.Id == receiverId && u.PartnerId == me, ct)
                        || await db.Users.AnyAsync(u => u.Id == me && u.PartnerId == receiverId, ct);
        if (!isFriend && !isPartner)
            return Problem(statusCode: 403, title: "Chỉ gửi được cho bạn bè hoặc partner");

        var when = scheduledAtUtc.Kind == DateTimeKind.Utc ? scheduledAtUtc : scheduledAtUtc.ToUniversalTime();
        if (when < DateTime.UtcNow.AddMinutes(-1))
            return Problem(statusCode: 400, title: "Giờ hẹn phải ở tương lai");

        // Trả lời lời nhắn khác: tối đa 2 vòng (gốc -> trả lời -> trả lời lại).
        int round = 0;
        int? rootId = null;
        if (replyToId is { } rid)
        {
            var parent = await db.VoiceNotes.AsNoTracking().FirstOrDefaultAsync(v => v.Id == rid, ct);
            if (parent is null) return NotFound();
            if (parent.ReceiverId != me)
                return Problem(statusCode: 403, title: "Không phải lời nhắn gửi cho bạn");
            round = parent.Round + 1;
            if (round > 2) return Problem(statusCode: 400, title: "Chỉ trả lời được 2 vòng");
            rootId = parent.RootId ?? parent.Id;
        }

        await using var stream = audio.OpenReadStream();
        var url = await storage.SaveAsync(stream, VoiceExtension(audio.ContentType)!, ct);

        var row = new VoiceNote
        {
            SenderId = me,
            ReceiverId = receiverId,
            AudioUrl = url,
            DurationMs = Math.Clamp(durationMs, 0, 120_000),
            ScheduledAtUtc = when,
            Round = round,
            RootId = rootId,
        };
        db.VoiceNotes.Add(row);
        await db.SaveChangesAsync(ct);

        var sender = await db.Users.AsNoTracking().Where(u => u.Id == me)
            .Select(u => new { u.FullName, u.AvatarUrl }).FirstAsync(ct);
        var receiverName = await db.Users.AsNoTracking().Where(u => u.Id == receiverId)
            .Select(u => u.FullName).FirstAsync(ct);

        // Xác nhận cho CHÍNH người gửi: biết đã hẹn được chưa và hẹn lúc nào.
        // Dùng độ lệch của AppClock để đúng múi giờ cấu hình, không hard-code.
        var localWhen = when + (clock.LocalNow - clock.UtcNow);
        db.Notifications.Add(new Notification
        {
            SenderId = null,
            ReceiverId = me,
            Title = "Đã hẹn gửi lời nhắn thoại",
            Message = $"Sẽ gửi tới {receiverName} lúc {localWhen:HH:mm} ngày {localWhen:dd/MM}. Vào Cá nhân → Hẹn lời nhắn thoại để huỷ nếu đổi ý.",
            Type = NotificationTypes.VoiceNote,
        });
        await db.SaveChangesAsync(ct);

        return new VoiceNoteDto(row.Id, me, sender.FullName, sender.AvatarUrl, row.AudioUrl,
            row.DurationMs, row.ScheduledAtUtc, null, 0, row.Round);
    }

    /// <summary>Lời nhắn đã tới giờ mà tôi chưa nghe (để app bật màn hình kêu).</summary>
    [HttpGet("incoming")]
    public async Task<List<VoiceNoteDto>> Incoming(CancellationToken ct)
    {
        var me = User.GetUserId();
        var now = DateTime.UtcNow;

        return await db.VoiceNotes
            .AsNoTracking()
            .Where(v => v.ReceiverId == me && v.ListenedAtUtc == null && !v.Cancelled
                        && v.ScheduledAtUtc <= now && v.ScheduledAtUtc > now.AddHours(-12))
            .OrderByDescending(v => v.ScheduledAtUtc)
            .Take(3)
            .Select(v => new VoiceNoteDto(
                v.Id, v.SenderId, v.Sender.FullName, v.Sender.AvatarUrl,
                v.AudioUrl, v.DurationMs, v.ScheduledAtUtc, v.ListenedAtUtc, v.RingCount, v.Round))
            .ToListAsync(ct);
    }

    /// <summary>Đã nghe → thôi kêu.</summary>
    [HttpPost("{id:int}/listen")]
    public async Task<IActionResult> Listen(int id, CancellationToken ct)
    {
        var me = User.GetUserId();
        var row = await db.VoiceNotes.FirstOrDefaultAsync(v => v.Id == id && v.ReceiverId == me, ct);
        if (row is null) return NotFound();

        row.ListenedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Huỷ một lời nhắn đã hẹn (chỉ người gửi).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var me = User.GetUserId();
        var row = await db.VoiceNotes.FirstOrDefaultAsync(v => v.Id == id && v.SenderId == me, ct);
        if (row is null) return NotFound();

        row.Cancelled = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Hộp thư lời nhắn thoại tôi ĐÃ NHẬN (để xem lại và nghe lại bất cứ lúc nào).</summary>
    [HttpGet("inbox")]
    public async Task<List<VoiceNoteDto>> Inbox(CancellationToken ct)
    {
        var me = User.GetUserId();
        return await db.VoiceNotes
            .AsNoTracking()
            .Where(v => v.ReceiverId == me)
            .OrderByDescending(v => v.ScheduledAtUtc)
            .Take(30)
            .Select(v => new VoiceNoteDto(
                v.Id, v.SenderId, v.Sender.FullName, v.Sender.AvatarUrl,
                v.AudioUrl, v.DurationMs, v.ScheduledAtUtc, v.ListenedAtUtc, v.RingCount, v.Round))
            .ToListAsync(ct);
    }

    /// <summary>Danh sách lời nhắn tôi đã hẹn gửi (gần nhất trước).</summary>
    [HttpGet]
    public async Task<List<VoiceNoteDto>> Sent(CancellationToken ct)
    {
        var me = User.GetUserId();
        return await db.VoiceNotes
            .AsNoTracking()
            .Where(v => v.SenderId == me)
            .OrderByDescending(v => v.CreatedAt)
            .Take(20)
            .Select(v => new VoiceNoteDto(
                v.Id, v.SenderId, v.Receiver.FullName, v.Receiver.AvatarUrl,
                v.AudioUrl, v.DurationMs, v.ScheduledAtUtc, v.ListenedAtUtc, v.RingCount, v.Round))
            .ToListAsync(ct);
    }
}