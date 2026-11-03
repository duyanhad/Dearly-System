using System.Collections.Concurrent;
using DrinkWater.Api.Data;
using DrinkWater.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

/// <summary>
/// Báo thức thức dậy chạy ở server: tới giờ đặt thì đẩy thông báo (web push tới được cả khi app
/// đóng) + realtime cho app đang mở, lặp mỗi 60 giây cho tới khi người dùng bấm tắt.
/// Kèm thông báo "đã ngủ đủ giấc" một lần cho mỗi giấc đang ngủ khi đủ số phút dự định.
/// </summary>
public sealed class SleepAlarmService(
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub,
    IClock clock,
    ILogger<SleepAlarmService> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(60);
    /// <summary>Kêu tối đa 45 phút kể từ giờ hẹn (quá thì coi như người dùng đã dậy).</summary>
    private const int RingMinutes = 45;
    private readonly ConcurrentDictionary<Guid, DateTime> _lastSent = new();

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
                log.LogWarning(ex, "Vòng báo thức giấc ngủ lỗi, sẽ thử lại sau");
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

        await RingWakeUpAsync(db, webPush, token);
        await NotifyEnoughSleepAsync(db, webPush, token);
    }

    private async Task RingWakeUpAsync(AppDbContext db, IWebPushSender webPush, CancellationToken token)
    {
        var now = clock.LocalNow;
        var today = DateOnly.FromDateTime(now);

        var rows = await db.HabitSettings
            .AsNoTracking()
            .Where(h => h.Kind == "sleep" && h.Enabled && h.AlarmEnabled)
            .Select(h => new { h.UserId, h.Times, h.AlarmRepeatPerRound, h.AlarmRounds, h.AlarmIntervalMin })
            .ToListAsync(token);
        if (rows.Count == 0) return;

        var userIds = rows.Select(r => r.UserId).ToList();
        var dismissed = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.SleepAlarmDismissedOn == today)
            .Select(u => u.Id)
            .ToListAsync(token);
        if (dismissed.Count == userIds.Count) return;

        foreach (var row in rows)
        {
            if (dismissed.Contains(row.UserId)) continue;

            var wake = (row.Times ?? string.Empty).Split(',')
                .Skip(1).FirstOrDefault(t => t.Trim().Length > 0)?.Trim();
            if (string.IsNullOrEmpty(wake) || !TimeOnly.TryParse(wake, out var wakeTime)) continue;

            var wakeAt = today.ToDateTime(wakeTime);
            if (now < wakeAt || now > wakeAt.AddMinutes(RingMinutes)) continue;

            if (_lastSent.TryGetValue(row.UserId, out var last) && now - last < Gap) continue;
            _lastSent[row.UserId] = now;

            var perRound = Math.Clamp(row.AlarmRepeatPerRound, 1, 20);
            var rounds = Math.Clamp(row.AlarmRounds, 1, 20);
            var gap = Math.Max(1, row.AlarmIntervalMin);
            var call = Math.Min(perRound * rounds, (int)((now - wakeAt).TotalMinutes / gap) + 1);
            var roundIndex = Math.Min(rounds, (call - 1) / perRound + 1);
            var callIndex = Math.Min(perRound, (call - 1) % perRound + 1);

            var title = "⏰ Báo thức Dearly";
            var body = $"Vòng {roundIndex}/{rounds} · lần {callIndex}/{perRound} — dậy thôi! "
                       + "Mở Dearly bấm \"Tắt báo thức\" để thôi kêu.";

            await hub.Clients.User(row.UserId.ToString()).SendAsync("ReceiveSleepAlarm", new
            {
                title,
                body,
                roundIndex,
                rounds,
                callIndex,
                perRound,
                wakeTime = wakeTime.ToString("HH:mm"),
            }, token);

            webPush.QueueForUser(row.UserId, title, body);
        }
    }

    private async Task NotifyEnoughSleepAsync(AppDbContext db, IWebPushSender webPush, CancellationToken token)
    {
        var nowUtc = clock.UtcNow;

        var sleeping = await db.SleepLogs
            .Where(l => l.Status == "sleeping" && l.FullNotifiedAtUtc == null && l.StartedAtUtc != null)
            .OrderBy(l => l.StartedAtUtc)
            .Take(200)
            .ToListAsync(token);
        if (sleeping.Count == 0) return;

        foreach (var sleep in sleeping)
        {
            var planned = Math.Clamp(sleep.PlannedMinutes, 0, 1080);
            if (planned <= 0) continue;

            var slept = (nowUtc - sleep.StartedAtUtc!.Value).TotalMinutes;
            if (slept < planned) continue;

            sleep.FullNotifiedAtUtc = nowUtc;
            var hours = planned / 60;
            var minutes = planned % 60;
            var length = minutes == 0 ? $"{hours} giờ" : $"{hours} giờ {minutes} phút";
            var title = "🌙 Đã ngủ đủ giấc";
            var body = $"Bạn đã ngủ đủ {length}. Báo thức buổi sáng vẫn kêu theo giờ đã đặt nhé.";

            await hub.Clients.User(sleep.UserId.ToString()).SendAsync("ReceiveSleepFull", new
            {
                title,
                body,
                plannedMinutes = planned,
            }, token);

            webPush.QueueForUser(sleep.UserId, title, body);
        }

        await db.SaveChangesAsync(token);
    }
}
