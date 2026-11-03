using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

/// <summary>
/// Canh hạn nhiệm vụ: nhắc người nhận trước hạn 60 phút và báo cả hai bên khi quá hạn chưa báo cáo.
/// Mỗi mốc chỉ nhắc một lần (đánh dấu vào chính nhiệm vụ).
/// </summary>
public sealed class DutyWatcher(
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub,
    IClock clock,
    ILogger<DutyWatcher> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SoonWindow = TimeSpan.FromMinutes(60);

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
                log.LogWarning(ex, "Vòng canh hạn nhiệm vụ lỗi, sẽ thử lại sau");
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

        var open = await db.Duties
            .Where(d => (d.Status == DutyStatuses.Open || d.Status == DutyStatuses.Rejected)
                        && (d.DueSoonNotifiedAtUtc == null || d.OverdueNotifiedAtUtc == null))
            .OrderBy(d => d.DueAtUtc)
            .Take(200)
            .ToListAsync(token);
        if (open.Count == 0) return;

        var changed = false;
        foreach (var duty in open)
        {
            if (duty.DueSoonNotifiedAtUtc is null && now > duty.DueAtUtc - SoonWindow && now < duty.DueAtUtc)
            {
                duty.DueSoonNotifiedAtUtc = now;
                changed = true;
                await SendAsync(db, hub, webPush, duty.AssigneeId,
                    "⏳ Sắp tới hạn nhiệm vụ",
                    $"“{duty.Title}” còn dưới 60 phút để báo cáo.", token);
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
