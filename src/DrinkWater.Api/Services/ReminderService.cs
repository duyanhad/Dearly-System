using System.Collections.Concurrent;
using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

/// <summary>
/// Vòng lặp nền: mỗi phút kiểm tra người dùng bật nhắc uống nước, tới giờ thì đẩy thông báo
/// (SignalR cho app đang mở + push cho điện thoại). Nhắc lại theo tính cách cho tới khi họ uống.
/// shortcut: mốc "lần nhắc gần nhất" giữ in-memory nên restart API sẽ nhắc sớm một lần.
/// </summary>
public sealed class ReminderService(
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub,
    IClock clock,
    ILogger<ReminderService> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<Guid, DateTime> _lastReminderUtc = new();

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
                log.LogWarning(ex, "Vòng nhắc uống nước lỗi, sẽ thử lại sau");
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
        var nowLocal = clock.LocalNow;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var push = scope.ServiceProvider.GetRequiredService<IPushSender>();
        var webPush = scope.ServiceProvider.GetRequiredService<IWebPushSender>();

        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.ReminderEnabled && !u.IsLocked)
            .Select(u => new
            {
                u.Id, u.ReminderStyle, u.ReminderIntervalMinutes, u.ReminderMl,
                u.ReminderStartHour, u.ReminderEndHour, u.FCMToken
            })
            .ToListAsync(token);
        if (users.Count == 0) return;

        var offset = nowLocal - clock.UtcNow;
        var todayLocalStart = nowLocal.Date;

        foreach (var user in users)
        {
            // Lần uống gần nhất (không giới hạn trong ngày) — để người vừa uống 23h đêm không bị nhắc lúc 7h sáng.
            var lastDrinkUtc = await db.WaterLogs
                .AsNoTracking()
                .Where(l => l.UserId == user.Id
                            && l.AmountMl > 0
                            && l.Status != LogStatuses.Rejected)
                .OrderByDescending(l => l.LogTime)
                .Select(l => (DateTime?)l.LogTime)
                .FirstOrDefaultAsync(token);

            var lastDrinkLocal = lastDrinkUtc is { } utc ? utc + offset : (DateTime?)null;
            var lastReminderLocal = _lastReminderUtc.TryGetValue(user.Id, out var at)
                ? at + offset
                : (DateTime?)null;

            if (!ReminderCadence.ShouldRemind(nowLocal, lastDrinkLocal, lastReminderLocal,
                    user.ReminderIntervalMinutes, user.ReminderStyle,
                    user.ReminderStartHour, user.ReminderEndHour))
                continue;

            var minutes = lastDrinkLocal is { } drink
                ? (int)(nowLocal - drink).TotalMinutes
                : (int)(nowLocal - todayLocalStart).TotalMinutes;
            var (title, message) = ReminderCadence.Message(user.ReminderStyle, user.ReminderMl, minutes);

            _lastReminderUtc[user.Id] = clock.UtcNow;

            await hub.Clients.User(user.Id.ToString()).SendAsync("ReceiveReminder", new
            {
                Title = title,
                Message = message,
                Style = user.ReminderStyle,
                AmountMl = user.ReminderMl
            }, token);

            if (!string.IsNullOrWhiteSpace(user.FCMToken))
                await push.SendAsync(user.FCMToken, title, message);

            // Web Push: tới được cả khi app/tab đã đóng (PWA trên iOS 16.4+).
            await webPush.SendToUserAsync(user.Id, title, message, user.ReminderStyle, ct: token);
        }
    }
}
