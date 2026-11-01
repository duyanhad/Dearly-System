using System.Collections.Concurrent;
using DrinkWater.Api.Data;
using DrinkWater.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

/// <summary>
/// Nhắc "sắp tới giờ đi ngủ": trước giờ ngủ theo số phút đã đặt (mặc định 10'), kêu lặp mỗi 60 giây
/// (push + realtime, kèm rung trên máy) cho tới khi người dùng vào tắt hoặc đã bắt đầu ngủ.
/// </summary>
public sealed class SleepReminderService(
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub,
    IClock clock,
    ILogger<SleepReminderService> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<Guid, DateTime> _lastSent = new();

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
                log.LogWarning(ex, "Vòng nhắc đi ngủ lỗi, sẽ thử lại sau");
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

        var now = clock.LocalNow;
        var today = DateOnly.FromDateTime(now);

        var rows = await db.HabitSettings
            .AsNoTracking()
            .Where(h => h.Kind == "sleep" && h.Enabled && h.AlarmEnabled)
            .Select(h => new { h.UserId, h.Times, h.RemindBeforeMin })
            .ToListAsync(token);

        if (rows.Count == 0) return;

        var userIds = rows.Select(r => r.UserId).ToList();
        var dismissed = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.SleepReminderDismissedOn == today)
            .Select(u => u.Id)
            .ToListAsync(token);
        // Đang ngủ rồi thì thôi nhắc.
        var sleeping = await db.SleepLogs
            .AsNoTracking()
            .Where(l => userIds.Contains(l.UserId) && l.Status == "sleeping")
            .Select(l => l.UserId)
            .ToListAsync(token);

        foreach (var row in rows)
        {
            if (dismissed.Contains(row.UserId) || sleeping.Contains(row.UserId)) continue;

            var bed = (row.Times ?? string.Empty).Split(',').FirstOrDefault(t => t.Trim().Length > 0)?.Trim();
            if (string.IsNullOrEmpty(bed) || !TimeOnly.TryParse(bed, out var bedTime)) continue;

            var remindBefore = Math.Clamp(row.RemindBeforeMin, 0, 240);
            var bedAt = today.ToDateTime(bedTime);
            var from = bedAt.AddMinutes(-remindBefore);
            var to = bedAt.AddMinutes(15);
            if (now < from || now > to) continue;

            if (_lastSent.TryGetValue(row.UserId, out var last) && now - last < Gap) continue;
            _lastSent[row.UserId] = now;

            var minutesLeft = (int)Math.Ceiling((bedAt - now).TotalMinutes);
            var title = minutesLeft > 0 ? $"Còn {minutesLeft} phút là tới giờ đi ngủ" : "Đã tới giờ đi ngủ";
            var body = "Chuẩn bị lên giường thôi — mở Dearly để bắt đầu giấc ngủ.";

            await hub.Clients.User(row.UserId.ToString()).SendAsync("ReceiveSleepReminder", new
            {
                title,
                body,
                minutesLeft = Math.Max(0, minutesLeft),
                bedTime = bedTime.ToString("HH:mm"),
            }, token);

            webPush.QueueForUser(row.UserId, title, body);
        }
    }
}