using DrinkWater.Api.Data;
using DrinkWater.Api.Entities;

namespace DrinkWater.Api.Services;

/// <summary>Writes the SystemLogs rows an admin later reads.</summary>
public sealed class SystemLogWriter(AppDbContext db, ILogger<SystemLogWriter> logger)
{
    public async Task WriteAsync(Guid? userId, string action, string? details,
        string level = LogLevels.Info, CancellationToken ct = default)
    {
        try
        {
            db.SystemLogs.Add(new SystemLog
            {
                UserId = userId,
                Action = action,
                Details = details,
                LogLevel = level,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Logging must never take the request down with it.
            logger.LogError(ex, "Không ghi được SystemLogs cho action {Action}", action);
        }
    }
}
