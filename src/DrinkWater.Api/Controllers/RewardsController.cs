using DrinkWater.Api.Data;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

public record NeedRewardDto(string Kind, int Amount, string CurrencyName, string CurrencyIcon, string CurrencyColor, DateTime At);

/// <summary>Tiền thưởng theo ngày của các nhu cầu (thuốc / bữa ăn / học tập / giấc ngủ / cân nặng).</summary>
[ApiController]
[Authorize]
[Route("api/rewards")]
public class RewardsController(AppDbContext db, RewardService rewards) : ControllerBase
{
    /// <summary>Phiếu đã đạt mục tiêu hôm nay và số tiền đã thưởng (rỗng = chưa đạt nhu cầu nào).</summary>
    [HttpGet("today")]
    public async Task<List<NeedRewardDto>> Today()
    {
        var userId = User.GetUserId();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var rows = await db.NeedRewards
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.Date == today)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        var groups = await db.EraGroups
            .AsNoTracking()
            .Include(g => g.Currency)
            .ToListAsync();

        return rows.Select(r =>
        {
            var group = groups.FirstOrDefault(g => g.Slug == r.Kind);
            var currency = group?.Currency;
            return new NeedRewardDto(
                r.Kind,
                r.Amount,
                currency?.Name ?? "giọt",
                currency?.IconKey ?? (currency is null ? "droplet" : "star"),
                currency?.Color ?? "#38bdf8",
                r.CreatedAt);
        }).ToList();
    }

    /// <summary>Tính lại thưởng hôm nay cho một nhu cầu (gọi sau khi ghi/duyệt phiếu).</summary>
    [HttpPost("{kind}/claim")]
    public async Task<ActionResult<int>> Claim(string kind)
    {
        var amount = await rewards.AwardIfReachedAsync(User.GetUserId(), kind);
        return amount;
    }
}
