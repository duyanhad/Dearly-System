using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

/// <summary>
/// Vòng kêu lời nhắn thoại hẹn giờ: mỗi 20 giây quét các note đã tới giờ mà người nhận chưa nghe
/// rồi bắn push + realtime. Cứ kêu tới khi họ bấm "đã nghe", hoặc dừng sau 20 phút để khỏi spam.
/// </summary>
public sealed class VoiceNoteRinger(
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub,
    ILogger<VoiceNoteRinger> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxRing = TimeSpan.FromMinutes(20);
    /// <summary>Giữ lời nhắn 7 ngày (reset mỗi tuần) để danh sách không bị quá nhiều.</summary>
    private const int RetentionDays = 7;
    private DateTime _lastPurge = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Vòng kêu lời nhắn thoại lỗi, sẽ thử lại sau");
            }

            try
            {
                await Task.Delay(Tick, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken token)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var webPush = scope.ServiceProvider.GetRequiredService<IWebPushSender>();

        var now = DateTime.UtcNow;
        var due = await db.VoiceNotes
            .Include(v => v.Sender)
            .Where(v => v.ListenedAtUtc == null && !v.Cancelled
                        && v.ScheduledAtUtc <= now
                        && (v.LastRungAtUtc == null || v.LastRungAtUtc < now - Gap))
            .OrderBy(v => v.ScheduledAtUtc)
            .Take(50)
            .ToListAsync(token);

        foreach (var note in due)
        {
            // Kêu quá lâu (người nhận không mở app) → dừng để không spam vô hạn.
            if (now - note.ScheduledAtUtc > MaxRing)
            {
                note.Cancelled = true;
                continue;
            }

            note.LastRungAtUtc = now;
            note.RingCount++;

            var title = $"Tin nhắn thoại từ {note.Sender.FullName}";
            var body = "Có lời nhắn thoại đang chờ bạn nghe — mở Dearly để nghe ngay.";

            // Lần kêu đầu tiên: ghi vào chuông thông báo để người nhận còn thấy lại về sau.
            if (note.RingCount == 1)
            {
                db.Notifications.Add(new Notification
                {
                    SenderId = note.SenderId,
                    ReceiverId = note.ReceiverId,
                    Title = title,
                    Message = body,
                    Type = NotificationTypes.VoiceNote,
                });
            }

            await hub.Clients.User(note.ReceiverId.ToString()).SendAsync("ReceiveVoiceNote", new
            {
                id = note.Id,
                senderName = note.Sender.FullName,
                senderAvatar = note.Sender.AvatarUrl,
                audioUrl = note.AudioUrl,
                durationMs = note.DurationMs,
                ringCount = note.RingCount,
            }, token);

            webPush.QueueForUser(note.ReceiverId, title, body);
        }

        await db.SaveChangesAsync(token);
        await PurgeOldAsync(db, now, token);
    }

    /// <summary>
    /// Dọn lời nhắn cũ: quá 7 ngày kể từ giờ hẹn (hoặc đã bị huỷ) thì xoá hẳn để danh sách
    /// không phình ra. Chạy tối đa mỗi giờ một lần cho nhẹ DB.
    /// shortcut: file ghi âm trên Storage không bị xoá theo, dọn định kỳ bằng script nếu cần.
    /// </summary>
    private async Task PurgeOldAsync(AppDbContext db, DateTime nowUtc, CancellationToken token)
    {
        if (nowUtc - _lastPurge < TimeSpan.FromMinutes(15)) return;
        _lastPurge = nowUtc;

        try
        {
            var cutoff = nowUtc.AddDays(-RetentionDays);
            var removed = await db.VoiceNotes
                .Where(v => v.ScheduledAtUtc < cutoff)
                .ExecuteDeleteAsync(token);
            if (removed > 0) log.LogInformation("Đã dọn {Count} lời nhắn thoại cũ hơn {Days} ngày", removed, RetentionDays);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Dọn lời nhắn thoại cũ lỗi");
        }
    }
}