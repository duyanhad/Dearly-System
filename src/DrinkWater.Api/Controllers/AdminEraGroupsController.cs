using System.Text;
using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

/// <summary>
/// Nhóm kỷ nguyên (Nước / Uống thuốc / Ăn / Ngủ…). Mỗi nhắc nhở sau này tra kỷ nguyên theo
/// <c>slug</c> của nhóm, nên admin tạo nhóm ở đây rồi gán kỷ nguyên vào nhóm đó.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/era-groups")]
public class AdminEraGroupsController(AppDbContext db, SystemLogWriter logs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EraGroupDto>>> List()
    {
        var groups = await db.EraGroups.AsNoTracking().Include(g => g.Currency).OrderBy(g => g.Order).ThenBy(g => g.Name).ToListAsync();
        var counts = await db.Eras
            .Where(e => e.GroupId != null)
            .GroupBy(e => e.GroupId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

        return groups.Select(g => g.ToDto(counts.GetValueOrDefault(g.Id))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<EraGroupDto>> Create(EraGroupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Problem(statusCode: 400, title: "Thiếu tên nhóm");

        var name = req.Name.Trim();
        var slug = Normalize(req.Slug, name);
        if (await db.EraGroups.AnyAsync(g => g.Slug == slug))
            return Problem(statusCode: 400, title: $"Slug \"{slug}\" đã dùng, chọn slug khác.");

        var group = new EraGroup
        {
            Name = name,
            Slug = slug,
            Color = string.IsNullOrWhiteSpace(req.Color) ? "#aedcf0" : req.Color.Trim(),
            Order = (await db.EraGroups.MaxAsync(g => (int?)g.Order) ?? 0) + 1,
            CurrencyId = await ResolveCurrencyAsync(req.CurrencyId),
            AwardBase = Math.Max(0, req.AwardBase ?? WaterCoins.BaseReward),
            AwardPerStreakDay = Math.Max(0, req.AwardPerStreakDay ?? WaterCoins.PerStreakDay),
        };
        db.EraGroups.Add(group);
        await db.SaveChangesAsync();
        await logs.WriteAsync(User.GetUserId(), "EraGroupCreate", $"Tạo nhóm kỷ nguyên {group.Name} ({group.Slug})");
        return group.ToDto(0);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EraGroupDto>> Update(Guid id, EraGroupRequest req)
    {
        var group = await db.EraGroups.Include(g => g.Currency).FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return Problem(statusCode: 400, title: "Thiếu tên nhóm");

        var slug = Normalize(req.Slug, req.Name.Trim());
        if (await db.EraGroups.AnyAsync(g => g.Slug == slug && g.Id != id))
            return Problem(statusCode: 400, title: $"Slug \"{slug}\" đã dùng, chọn slug khác.");

        group.Name = req.Name.Trim();
        group.Slug = slug;
        if (!string.IsNullOrWhiteSpace(req.Color)) group.Color = req.Color.Trim();
        // Luôn áp dụng: gửi null = quay về tiền mặc định (Giọt), nếu bỏ qua thì không thể bỏ gán tiền.
        group.CurrencyId = await ResolveCurrencyAsync(req.CurrencyId);
        if (req.AwardBase is not null) group.AwardBase = Math.Max(0, req.AwardBase.Value);
        if (req.AwardPerStreakDay is not null) group.AwardPerStreakDay = Math.Max(0, req.AwardPerStreakDay.Value);
        await db.SaveChangesAsync();

        if (group.CurrencyId is not null)
            await db.Entry(group).Reference(g => g.Currency).LoadAsync();

        var count = await db.Eras.CountAsync(e => e.GroupId == id);
        return group.ToDto(count);
    }

    /// <summary>Xoá nhóm; kỷ nguyên trong nhóm chỉ bị bỏ gán nhóm, không bị xoá.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var group = await db.EraGroups.Include(g => g.Currency).FirstOrDefaultAsync(g => g.Id == id);
        if (group is null) return NotFound();

        await db.Eras.Where(e => e.GroupId == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.GroupId, (Guid?)null));
        db.EraGroups.Remove(group);
        await db.SaveChangesAsync();
        await logs.WriteAsync(User.GetUserId(), "EraGroupDelete", $"Xoá nhóm kỷ nguyên {group.Name}");
        return NoContent();
    }

    /// <summary>Chỉ nhận tiền có thật (và còn bật), tránh lỗi khoá ngoại khi tiền vừa bị xoá.</summary>
    private async Task<Guid?> ResolveCurrencyAsync(Guid? currencyId)
    {
        if (currencyId is null || currencyId == Guid.Empty) return null;
        var exists = await db.Currencies.AnyAsync(c => c.Id == currencyId && c.IsActive);
        return exists ? currencyId : null;
    }

    /// <summary>Bỏ dấu tiếng Việt để slug dùng được trong URL/API.</summary>
    private static string Normalize(string? slug, string name)
    {
        var raw = string.IsNullOrWhiteSpace(slug) ? name : slug.Trim();
        var folded = new StringBuilder();
        foreach (var c in raw.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c)) folded.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or '_') folded.Append('-');
        }
        var value = System.Text.RegularExpressions.Regex.Replace(folded.ToString(), "-{2,}", "-").Trim('-');
        return value.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : value[..Math.Min(40, value.Length)];
    }
}
