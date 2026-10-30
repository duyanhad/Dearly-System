namespace DrinkWater.Api.Dtos;

/// <summary>Một cấp linh vật trong kỷ nguyên (phía người dùng).</summary>
public record EraLevelDto(
    int Level,
    string Name,
    string Description,
    string Artifact,
    int Price,
    string? ImageUrl,
    bool Unlocked);

public record EraDto(
    Guid Id,
    int Order,
    string Title,
    string Subtitle,
    string BgFrom,
    string BgTo,
    int UnlockPrice,
    bool IsActive,
    string? GroupSlug,
    string? GroupName,
    string? GroupColor,
    int GroupAwardBase,
    int GroupAwardPerStreakDay,
    CurrencyDto Currency,
    int UnlockedLevel,
    bool Unlocked,
    bool CanAfford,
    List<EraLevelDto> Levels);

/// <summary>Loại tiền + số dư của người dùng (nếu có).</summary>
public record CurrencyDto(Guid Id, string Code, string Name, string IconKey, string Color, bool IsDefault, int Amount);

/// <summary>Tất cả kỷ nguyên + số dư từng loại tiền + luật phát điểm mặc định.</summary>
public record EraCatalogDto(
    int Coins,
    int RewardBase,
    int RewardPerStreakDay,
    List<CurrencyDto> Currencies,
    List<EraDto> Eras);

// ---------- admin ----------

public record AdminEraLevelDto(
    int Level,
    string Name,
    string Description,
    string Artifact,
    int Price,
    string? ImageUrl);

public record AdminEraDto(
    Guid Id,
    int Order,
    string Title,
    string Subtitle,
    string BgFrom,
    string BgTo,
    int UnlockPrice,
    bool IsActive,
    Guid? GroupId,
    string? GroupName,
    List<AdminEraLevelDto> Levels);

public record AdminEraLevelRequest(
    int Level,
    string Name,
    string? Description,
    string? Artifact,
    int Price,
    string? ImageUrl);

public record AdminEraRequest(
    int Order,
    string Title,
    string? Subtitle,
    string? BgFrom,
    string? BgTo,
    int UnlockPrice,
    bool IsActive,
    Guid? GroupId,
    List<AdminEraLevelRequest>? Levels);

/// <summary>Đổi thứ tự kỷ nguyên bằng kéo thả: gửi danh sách id theo đúng thứ tự mong muốn.</summary>
public record EraOrderRequest(List<Guid> Ids);

// ---------- nhóm kỷ nguyên ----------

public record EraGroupDto(Guid Id, string Name, string Slug, string Color, int Order, int EraCount, Guid? CurrencyId, string? CurrencyCode, string? CurrencyName, int AwardBase, int AwardPerStreakDay);

public record EraGroupRequest(string Name, string? Slug, string? Color, Guid? CurrencyId, int? AwardBase, int? AwardPerStreakDay);

public record CurrencyRequest(string Name, string? Code, string? IconKey, string? Color, bool? IsActive);

/// <summary>Tặng điểm cho một người (admin), chủ yếu để thử luật phát điểm.</summary>
public record GrantCurrencyRequest(string Email, int Amount);

/// <summary>Kết quả mở khoá / nâng cấp.</summary>
public record EraActionResultDto(int Coins, int UnlockedLevel, string? ImageUrl, string? Name);
