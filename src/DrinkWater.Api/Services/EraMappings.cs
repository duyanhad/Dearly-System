using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;

namespace DrinkWater.Api.Services;

public static class EraMappings
{
    public static EraLevelDto ToDto(this EraLevel l, int unlockedLevel) => new(
        l.Level,
        l.Name,
        l.Description,
        l.Artifact,
        l.Price,
        l.ImageUrl,
        l.Level <= unlockedLevel);

    public static EraDto ToDto(this Era era, int unlockedLevel, int amount, CurrencyDto currency) => new(
        era.Id,
        era.Order,
        era.Title,
        era.Subtitle,
        era.BgFrom,
        era.BgTo,
        era.UnlockPrice,
        era.IsActive,
        era.Group?.Slug,
        era.Group?.Name,
        era.Group?.Color,
        era.Group?.AwardBase ?? WaterCoins.BaseReward,
        era.Group?.AwardPerStreakDay ?? WaterCoins.PerStreakDay,
        currency,
        unlockedLevel,
        // mở kỷ nguyên = đã có ít nhất cấp 1
        unlockedLevel > 0,
        amount >= era.UnlockPrice || unlockedLevel > 0,
        era.Levels.OrderBy(l => l.Level).Select(l => l.ToDto(unlockedLevel)).ToList());

    public static CurrencyDto ToDto(this Currency c, int amount) =>
        new(c.Id, c.Code, c.Name, c.IconKey, c.Color, c.IsDefault, amount);

    public static AdminEraDto ToAdminDto(this Era era) => new(
        era.Id,
        era.Order,
        era.Title,
        era.Subtitle,
        era.BgFrom,
        era.BgTo,
        era.UnlockPrice,
        era.IsActive,
        era.GroupId,
        era.Group?.Name,
        era.Levels.OrderBy(l => l.Level)
            .Select(l => new AdminEraLevelDto(l.Level, l.Name, l.Description, l.Artifact, l.Price, l.ImageUrl))
            .ToList());

    public static EraGroupDto ToDto(this EraGroup g, int eraCount) => new(
        g.Id,
        g.Name,
        g.Slug,
        g.Color,
        g.Order,
        eraCount,
        g.CurrencyId,
        g.Currency?.Code,
        g.Currency?.Name,
        g.AwardBase,
        g.AwardPerStreakDay);
}
