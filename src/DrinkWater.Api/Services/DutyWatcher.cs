using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

/// <summary>
/// Canh nhiệm vụ:
/// - Chưa bấm "đã nhận nhiệm vụ" → kêu lặp mỗi 60 giây cho tới khi nhận (tối đa 60 lần).
/// - Nhắc trước giờ bắt đầu theo số phút người giao chọn.
/// - Báo cả hai bên khi quá hạn báo cáo.
/// Mỗi mốc chỉ nhắc một lần (đánh dấu vào chính nhiệm vụ).
/// </summary>
public sealed class DutyWatcher(
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub,
    IClock clock,
    ILogger<DutyWatcher> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    /// <summary>Khoảng cách giữa hai lần kêu "nhận nhiệm vụ".</summary>
    private static readonly TimeSpan RingGap = TimeSpan.FromSeconds(60);
    /// <summary>Kêu tối đa 60 lần (1 tiếng) để không spam vô hạn.</summary>
    private const int MaxRings = 60;

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
                log.LogWarning(ex, "Vòng canh nhiệm vụ lỗi, sẽ thử lại sau");
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
        var now = clock.UtcNow;

        await RingUnacceptedAsync(db, hub, webPush, now, token);
        await WarnAsync(db, hub, webPush, now, token);
    }

    /// <summary>Kêu lặp cho tới khi người nhận bấm "đã nhận nhiệm vụ".</summary>
    private static async Task RingUnacceptedAsync(
        AppDbContext db, IHubContext<NotificationHub> hub, IWebPushSender webPush, DateTime now, CancellationToken token)
    {
        var pending = await db.Duties
            .Where(d => d.AcceptedAtUtc == null
                        && d.Status == DutyStatuses.Open
                        && d.RingCount < MaxRings
                        && (d.LastRungAtUtc == null || d.LastRungAtUtc < now.Add(-RingGap)))
            .OrderBy(d => d.CreatedAt)
            .Take(100)
            .ToListAsync(token);
        if (pending.Count == 0) return;

        foreach (var duty in pending)
        {
            duty.LastRungAtUtc = now;
            duty.RingCount += 1;

            var title = "📋 Nhận nhiệm vụ đi!";
            var body = $"“{duty.Title}” đang chờ bạn bấm “Đã nhận nhiệm vụ” (lần {duty.RingCount}).";

            await hub.Clients.User(duty.AssigneeId.ToString())
                .SendAsync("ReceiveDutyRing", new { id = duty.Id, title, body }, token);
            webPush.QueueForUser(duty.AssigneeId, title, body);
        }

        await db.SaveChangesAsync(token);
    }

    /// <summary>Nhắc trước giờ bắt đầu + báo quá hạn (mỗi mốc một lần).</summary>
    private static async Task WarnAsync(
        AppDbContext db, IHubContext<NotificationHub> hub, IWebPushSender webPush, DateTime now, CancellationToken token)
    {
        var open = await db.Duties
            .Where(d => d.Status == DutyStatuses.Open || d.Status == DutyStatuses.Rejected)
            .Where(d => d.StartNotifiedAtUtc == null || d.OverdueNotifiedAtUtc == null)
            .OrderBy(d => d.StartsAtUtc)
            .Take(200)
            .ToListAsync(token);
        if (open.Count == 0) return;

        var changed = false;
        foreach (var duty in open)
        {
            var before = TimeSpan.FromMinutes(Math.Clamp(duty.RemindBeforeMin, 0, 240));
            if (duty.StartNotifiedAtUtc is null && duty.RemindBeforeMin > 0
                && now > duty.StartsAtUtc - before && now < duty.StartsAtUtc)
            {
                duty.StartNotifiedAtUtc = now;
                changed = true;
                await SendAsync(db, hub, webPush, duty.AssigneeId,
                    "⏰ Sắp tới giờ bắt đầu nhiệm vụ",
                    $"“{duty.Title}” bắt đầu sau {duty.RemindBeforeMin} phút.", token);
            }

            if (duty.OverdueNotifiedAtUtc is null && now > duty.DueAtUtc)
            {
                duty.OverdueNotifiedAtUtc = now;
                changed = true;
                await SendAsync(db, hub, webPush, duty.AssigneeId,
                    "⚠️ Nhiệm vụ quá hạn",
                    $"“{duty.Title}” đã quá hạn. Vẫn báo cáo được, hệ thống sẽ ghi là báo cáo muộn.", token);
                await SendAsync(db, hub, webPush, duty.AssignerId,
                    "⚠️ Nhiệm vụ quá hạn",
                    $"“{duty.Title}” đã quá hạn mà chưa có báo cáo.", token);
            }
        }

        if (changed) await db.SaveChangesAsync(token);
    }

    private static async Task SendAsync(
        AppDbContext db, IHubContext<NotificationHub> hub, IWebPushSender webPush,
        Guid userId, string title, string body, CancellationToken token)
    {
        db.Notifications.Add(new Notification
        {
            ReceiverId = userId,
            Title = title,
            Message = body,
            Type = NotificationTypes.Duty,
        });
        await hub.Clients.User(userId.ToString()).SendAsync("ReceiveDuty", new { title, body }, token);
        webPush.QueueForUser(userId, title, body);
    }
}
