namespace DrinkWater.Api.Services;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "DrinkWater";
    public string Audience { get; set; } = "DrinkWaterClient";
    public string Key { get; set; } = string.Empty;
    public int ExpireHours { get; set; } = 12;
}

public sealed class AppOptions
{
    public string TimeZone { get; set; } = "Asia/Ho_Chi_Minh";
    public SeedAdminOptions SeedAdmin { get; set; } = new();
}

public sealed class SeedAdminOptions
{
    public string Email { get; set; } = "admin@drinkwater.local";
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = "Administrator";
}

/// <summary>Dịch vụ tách nền rembg (Python sidecar) — tuỳ chọn, không có thì client tự tách.</summary>
public sealed class RembgOptions
{
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = "http://127.0.0.1:7000";
    public string Model { get; set; } = "birefnet-general";
    public bool DecontaminateEdges { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 180;
}

/// <summary>Web Push (VAPID) - gui thong bao he thong khi app da dong.</summary>
public sealed class WebPushOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>mailto: hoac https URL, bat buoc theo chuan VAPID.</summary>
    public string Subject { get; set; } = "mailto:admin@drinkwater.local";
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public bool IsConfigured => Enabled && !string.IsNullOrEmpty(PublicKey) && !string.IsNullOrEmpty(PrivateKey);
}