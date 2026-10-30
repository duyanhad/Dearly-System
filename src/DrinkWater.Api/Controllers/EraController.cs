using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

/// <summary>Kỷ nguyên linh vật: xem, mở khoá, nâng cấp bằng tiền của nhóm (mặc định là "giọt").</summary>
[ApiController]
[Authorize]
[Route("api/eras")]
public class EraController(AppDbContext db) : ControllerBase
{
    /// <param name="group">Lọc theo slug nhóm (water, medicine, meal, sleep…) — bỏ trống = tất cả.</param>
    [HttpGet]
    public async Task<ActionResult<EraCatalogDto>> Get([FromQuery] string? group = null)
    {
        var userId = User.GetUserId();
        var user = await db.Users.FindAsync(userId);
        if (user is null) return NotFound();

        var query = db.Eras
            .AsNoTracking()
            .Where(e => e.IsActive)
            .Include(e => e.Levels)
            .Include(e => e.Group).ThenInclude(g => g!.Currency)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(group))
        {
            var slug = group.Trim().ToLowerInvariant();
            query = query.Where(e => e.Group != null && e.Group.Slug == slug);
        }

        var eras = await query.OrderBy(e => e.Order).ToListAsync();

        var progress = await db.UserEraProgresses
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.EraId, p => p.UnlockedLevel);

        // Kỷ nguyên miễn phí (giá 0) tự mở cấp 1 cho mọi người.
        var freeEras = eras.Where(e => e.UnlockPrice <= 0 && progress.GetValueOrDefault(e.Id) == 0).ToList();
        if (freeEras.Count > 0)
        {
            foreach (var era in freeEras)
            {
                db.UserEraProgresses.Add(new UserEraProgress { UserId = userId, EraId = era.Id, UnlockedLevel = 1 });
                progress[era.Id] = 1;
            }

            await db.SaveChangesAsync();
        }

        var economy = await LoadEconomyAsync(userId, user.Coins);
        var items = eras
            .Select(e =>
            {
                var currency = CurrencyOf(e.Group, economy.ByCode);
                var amount = currency.IsDefault ? user.Coins : economy.AmountOf(currency.Id);
                return e.ToDto(progress.GetValueOrDefault(e.Id), amount, currency.ToDto(amount));
            })
            .ToList();

        return new EraCatalogDto(
            user.Coins,
            WaterCoins.BaseReward,
            WaterCoins.PerStreakDay,
            economy.All,
            items);
    }

    /// <summary>Mở khoá kỷ nguyên (mở luôn cấp 1).</summary>
    [HttpPost("{eraId:guid}/unlock")]
    public async Task<ActionResult<EraActionResultDto>> Unlock(Guid eraId)
    {
        var user = await db.Users.FindAsync(User.GetUserId());
        if (user is null) return NotFound();

        var era = await FindEraAsync(eraId);
        if (era is null) return NotFound();

        var progress = await db.UserEraProgresses.FindAsync(user.Id, eraId);
        if (progress is { UnlockedLevel: > 0 })
            return Problem(statusCode: 400, title: "Kỷ nguyên này đã mở rồi");

        var (currency, amount) = await BalanceAsync(user, era);
        if (amount < era.UnlockPrice)
            return Problem(statusCode: 400, title: $"Cần {era.UnlockPrice} {currency.Name.ToLowerInvariant()}, bạn có {amount}");

        var left = await SpendAsync(user, currency, amount - era.UnlockPrice);
        if (progress is null)
        {
            progress = new UserEraProgress { UserId = user.Id, EraId = eraId, UnlockedLevel = 1 };
            db.UserEraProgresses.Add(progress);
        }
        else
        {
            progress.UnlockedLevel = 1;
            progress.UnlockedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        var first = era.Levels.OrderBy(l => l.Level).FirstOrDefault();
        return new EraActionResultDto(left, progress.UnlockedLevel, first?.ImageUrl, first?.Name);
    }

    /// <summary>Nâng lên cấp kế tiếp trong kỷ nguyên.</summary>
    [HttpPost("{eraId:guid}/upgrade")]
    public async Task<ActionResult<EraActionResultDto>> Upgrade(Guid eraId)
    {
        var user = await db.Users.FindAsync(User.GetUserId());
        if (user is null) return NotFound();

        var era = await FindEraAsync(eraId);
        if (era is null) return NotFound();

        var progress = await db.UserEraProgresses.FindAsync(user.Id, eraId);
        if (progress is null || progress.UnlockedLevel <= 0)
            return Problem(statusCode: 400, title: "Chưa mở kỷ nguyên này");

        var nextLevel = progress.UnlockedLevel + 1;
        var next = era.Levels.FirstOrDefault(l => l.Level == nextLevel);
        if (next is null)
            return Problem(statusCode: 400, title: "Đã đạt cấp cao nhất của kỷ nguyên");

        var (currency, amount) = await BalanceAsync(user, era);
        if (amount < next.Price)
            return Problem(statusCode: 400, title: $"Cần {next.Price} {currency.Name.ToLowerInvariant()}, bạn có {amount}");

        var left = await SpendAsync(user, currency, amount - next.Price);
        progress.UnlockedLevel = nextLevel;
        progress.UnlockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return new EraActionResultDto(left, progress.UnlockedLevel, next.ImageUrl, next.Name);
    }

    // ---------- tiện ích ----------

    private Task<Era?> FindEraAsync(Guid eraId) =>
        db.Eras
            .Include(e => e.Levels)
            .Include(e => e.Group).ThenInclude(g => g!.Currency)
            .FirstOrDefaultAsync(e => e.Id == eraId && e.IsActive);

    /// <summary>Tiền của nhóm chứa kỷ nguyên; nhóm chưa gán tiền thì dùng tiền mặc định.</summary>
    private static Currency CurrencyOf(EraGroup? group, Dictionary<string, Currency> byCode) =>
        group?.Currency ?? byCode["drops"];

    private async Task<(Currency currency, int amount)> BalanceAsync(User user, Era era)
    {
        var economy = await LoadEconomyAsync(user.Id, user.Coins);
        var currency = CurrencyOf(era.Group, economy.ByCode);
        return (currency, currency.IsDefault ? user.Coins : economy.AmountOf(currency.Id));
    }

    /// <summary>Trừ tiền (mặc định ghi vào cột Coins, loại khác ghi bảng số dư) và trả về số còn lại.</summary>
    private async Task<int> SpendAsync(User user, Currency currency, int remaining)
    {
        if (currency.IsDefault)
        {
            user.Coins = remaining;
            return remaining;
        }

        var row = await db.UserCurrencies.FindAsync(user.Id, currency.Id);
        if (row is null)
        {
            db.UserCurrencies.Add(new UserCurrency { UserId = user.Id, CurrencyId = currency.Id, Amount = remaining });
        }
        else
        {
            row.Amount = remaining;
        }

        return remaining;
    }

    private async Task<Economy> LoadEconomyAsync(Guid userId, int coins)
    {
        var currencies = await db.Currencies.Where(c => c.IsActive).OrderBy(c => c.Order).ToListAsync();
        var balances = await db.UserCurrencies
            .Where(x => x.UserId == userId)
            .ToDictionaryAsync(x => x.CurrencyId, x => x.Amount);

        var byCode = currencies.ToDictionary(c => c.Code);
        var all = currencies
            .Select(c => c.ToDto(c.IsDefault ? coins : balances.GetValueOrDefault(c.Id)))
            .ToList();

        return new Economy(byCode, all, balances);
    }

    private sealed record Economy(Dictionary<string, Currency> ByCode, List<CurrencyDto> All, Dictionary<Guid, int> Balances)
    {
        public int AmountOf(Guid currencyId) => Balances.GetValueOrDefault(currencyId);
    }
}
