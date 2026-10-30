namespace DrinkWater.Api.Services;

public interface IPushSender
{
    Task SendAsync(string? fcmToken, string title, string body, CancellationToken ct = default);
}

/// <summary>Stand-in until a Firebase service account exists: writes the push to the log
/// so the nudge/broadcast flow can be verified end to end.</summary>
public sealed class LogPushSender(ILogger<LogPushSender> logger) : IPushSender
{
    public Task SendAsync(string? fcmToken, string title, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fcmToken))
        {
            logger.LogInformation("[push skipped] no token — {Title}: {Body}", title, body);
            return Task.CompletedTask;
        }

        logger.LogInformation("[push mock] token {Token} — {Title}: {Body}",
            fcmToken[..Math.Min(12, fcmToken.Length)], title, body);
        return Task.CompletedTask;
    }
}
