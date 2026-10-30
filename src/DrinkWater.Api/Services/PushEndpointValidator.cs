namespace DrinkWater.Api.Services;

/// <summary>
/// Chỉ cho phép endpoint thuộc các dịch vụ Web Push hợp lệ.
/// Chặn SSRF: nếu không kiểm tra, server sẽ POST tới URL do người dùng nhập
/// (loopback, mạng nội bộ, cloud metadata…).
/// </summary>
public static class PushEndpointValidator
{
    // Đuôi miền chính thức của các push service (FCM, Mozilla, WNS, Apple, Edge).
    private static readonly string[] AllowedHosts =
    [
        "fcm.googleapis.com",
        "android.googleapis.com",
        "push.services.mozilla.com",
        "updates.push.services.mozilla.com",
        "notify.windows.com",
        "push.apple.com",
        "web.push.apple.com",
        "notificationservice.push.apple.com"
    ];

    private static readonly string[] AllowedSuffixes =
    [
        ".push.services.mozilla.com",
        ".notify.windows.com",
        ".push.apple.com",
        ".notificationservice.push.apple.com",
        ".mozaws.net"
    ];

    public static bool IsAllowed(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return false;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        // Endpoint phải là URL công khai: chặn IP literal và localhost.
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6) return false;

        var host = uri.Host.ToLowerInvariant();
        if (host is "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal)) return false;

        return AllowedHosts.Contains(host)
               || AllowedSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal));
    }
}
