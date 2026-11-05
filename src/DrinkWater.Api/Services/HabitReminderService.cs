using System.Collections.Concurrent;
using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Services;

/// <summary>
/// Nhắc các nhu cầu đặt giờ (thuốc, bữa ăn, học tập, cân nặng): tới mốc giờ đã đặt thì đẩy
/// SignalR cho app đang mở + Web Push cho máy đã đóng app. Mỗi mốc chỉ nhắc một lần/ngày.
/// shortcut: mốc "đã nhắc" giữ in-memory nên restart API có thể nhắc lại một lần trong cửa sổ 2 phút.
/// </summary>
public sealed class HabitReminderService(
    IServiceScopeFactory scopes,
    IHubContext<NotificationHub> hub,
    IClock clock,
    ILogger<HabitReminderService> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    /// <summary>Cửa sổ để bắt kịp mốc giờ dù vòng lặp chạy lệch vài chục giây.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);
    private static readonly string[] Kinds = ["medicine", "meal", "study", "weight"];
    private readonly ConcurrentDictionary<string, DateTime> _sent = new();

    private static readonly Dictionary<string, (string Title, string Verb)> Text = new()
    {
        ["medicine"] = ("💊 Đến giờ uống thuốc", "uống thuốc"),
        ["meal"] = ("🍚 Đến giờ ăn", "ăn"),
        ["study"] = ("📚 Đến giờ học", "học"),
        ["weight"] = ("⚖️ Giờ cân nặng", "cân"),
    };

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
                log.LogWarning(ex, "Vòng nhắc nhu cầu đặt giờ lỗi, sẽ thử lại sau");
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
        var now = clock.LocalNow;
        var today = DateOnly.FromDateTime(now);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var webPush = scope.ServiceProvider.GetRequiredService<IWebPushSender>();

        var rows = await db.HabitSettings
            .AsNoTracking()
            .Where(h => h.Enabled
                        && (h.Kind == "medicine" || h.Kind == "meal" || h.Kind == "study" || h.Kind == "weight")
                        && h.Times != null && h.Times != "")
            .Select(h => new { h.UserId, h.Kind, h.Times, h.RemindBeforeMin, h.TargetValue })
            .ToListAsync(token);
        if (rows.Count == 0) return;

        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var doneLogs = await db.HabitLogs
            .AsNoTracking()
            .Where(l => userIds.Contains(l.UserId) && l.Date == today && l.Status != "rejected")
            .Select(l => new { l.UserId, l.Kind, l.MealSlot })
            .ToListAsync(token);
        // Bữa ăn / thuốc: chỉ ca chính mới tính là đã làm (bữa phụ, ăn vặt không làm tắt nhắc).
        var doneMap = doneLogs
            .Where(l => HabitProgress.CountsForGoal(l.Kind, l.MealSlot))
            .GroupBy(l => (l.UserId, l.Kind))
            .ToDictionary(
                g => g.Key,
                g => g.Key.Kind is "meal" or "medicine"
                    ? g.Select(x => x.MealSlot).Distinct().Count()
                    : g.Count());

        foreach (var row in rows)
        {
            // Đã đủ số lần hôm nay thì thôi nhắc (cân nặng đo bằng gam nên không xét).
            if (row.Kind != "weight" && row.TargetValue > 0
                && doneMap.GetValueOrDefault((row.UserId, row.Kind)) >= row.TargetValue)
                continue;

            var remindBefore = Math.Clamp(row.RemindBeforeMin, 0, 240);
            foreach (var raw in (row.Times ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TimeOnly.TryParse(raw.Trim(), out var slot)) continue;
                var at = today.ToDateTime(slot).AddMinutes(-remindBefore);
                if (now < at || now > at.Add(Window)) continue;

                var key = $"{row.UserId}|{row.Kind}|{slot:HH:mm}|{today:yyyy-MM-dd}";
                if (!_sent.TryAdd(key, now)) continue;

                var (title, verb) = Text.TryGetValue(row.Kind, out var t) ? t : ("Nhắc nhở Dearly", "ghi lại");
                var body = remindBefore > 0
                    ? $"Còn {remindBefore} phút là tới giờ {verb} ({slot:HH:mm}). Mở Dearly để ghi lại nhé."
                    : $"Tới giờ {verb} rồi ({slot:HH:mm}). Mở Dearly để ghi lại nhé.";

                await hub.Clients.User(row.UserId.ToString()).SendAsync("ReceiveHabitReminder", new
                {
                    kind = row.Kind,
                    title,
                    body,
                    time = slot.ToString("HH:mm"),
                    minutesBefore = remindBefore,
                }, token);

                webPush.QueueForUser(row.UserId, title, body);
                log.LogInformation("Nhắc {Kind} cho {UserId} lúc {Time}", row.Kind, row.UserId, slot.ToString("HH:mm"));

                // Lưu vào chuông thông báo để xem lại được (và để kiểm chứng service có chạy).
                db.Notifications.Add(new Entities.Notification
                {
                    ReceiverId = row.UserId,
                    Title = title,
                    Message = body,
                    Type = Entities.NotificationTypes.Reminder,
                });
            }
        }

        await db.SaveChangesAsync(token);

        // Dọn mốc của hôm trước để dictionary không phình mãi.
        if (_sent.Count > 4000)
        {
            var prefix = today.ToString("yyyy-MM-dd");
            foreach (var key in _sent.Keys)
            {
                if (!key.EndsWith(prefix, StringComparison.Ordinal)) _sent.TryRemove(key, out _);
            }
        }
    }
}
