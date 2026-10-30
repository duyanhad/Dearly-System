using System.Text;
using System.Text.RegularExpressions;
using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Api.Controllers;

/// <summary>
/// Tiền tệ trong game (giọt, sao ngủ, viên thuốc…). Admin tự tạo loại tiền mới rồi gán cho nhóm
/// kỷ nguyên; nhóm nào dùng tiền nào thì mở/nâng cấp kỷ nguyên trong nhóm đó trả bằng tiền đó.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin/currencies")]
public class AdminCurrenciesController(AppDbContext db, SystemLogWriter logs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CurrencyDto>>> List()
    {
        var rows = await db.Currencies.AsNoTracking().OrderBy(c => c.Order).ThenBy(c => c.Name).ToListAsync();
        var used = await db.EraGroups
            .Where(g => g.CurrencyId != null)
            .GroupBy(g => g.CurrencyId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Count);

        return rows.Select(c => c.ToDto(used.GetValueOrDefault(c.Id))).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<CurrencyDto>> Create(CurrencyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Problem(statusCode: 400, title: "Thiếu tên tiền");

        var code = Normalize(req.Code, req.Name);
        if (await db.Currencies.AnyAsync(c => c.Code == code))
            return Problem(statusCode: 400, title: $"Mã \"{code}\" đã dùng, chọn mã khác.");

        var currency = new Currency
        {
            Code = code,
            Name = req.Name.Trim(),
            IconKey = string.IsNullOrWhiteSpace(req.IconKey) ? "star" : req.IconKey.Trim(),
            Color = string.IsNullOrWhiteSpace(req.Color) ? "#f0b429" : req.Color.Trim(),
            Order = (await db.Currencies.MaxAsync(c => (int?)c.Order) ?? 0) + 1,
        };
        db.Currencies.Add(currency);
        await db.SaveChangesAsync();
        await logs.WriteAsync(User.GetUserId(), "CurrencyCreate", $"Tạo tiền {currency.Name} ({currency.Code})");
        return currency.ToDto(0);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CurrencyDto>> Update(Guid id, CurrencyRequest req)
    {
        var currency = await db.Currencies.FirstOrDefaultAsync(c => c.Id == id);
        if (currency is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return Problem(statusCode: 400, title: "Thiếu tên tiền");

        var code = Normalize(req.Code, req.Name);
        if (await db.Currencies.AnyAsync(c => c.Code == code && c.Id != id))
            return Problem(statusCode: 400, title: $"Mã \"{code}\" đã dùng, chọn mã khác.");

        currency.Name = req.Name.Trim();
        currency.Code = code;
        if (!string.IsNullOrWhiteSpace(req.IconKey)) currency.IconKey = req.IconKey.Trim();
        if (!string.IsNullOrWhiteSpace(req.Color)) currency.Color = req.Color.Trim();
        if (req.IsActive is not null) currency.IsActive = req.IsActive.Value;
        await db.SaveChangesAsync();

        var used = await db.EraGroups.CountAsync(g => g.CurrencyId == id);
        return currency.ToDto(used);
    }

    /// <summary>Xoá tiền; nhóm đang dùng sẽ quay về tiền mặc định (giọt).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var currency = await db.Currencies.FirstOrDefaultAsync(c => c.Id == id);
        if (currency is null) return NotFound();
        if (currency.IsDefault) return Problem(statusCode: 400, title: "Không xoá được tiền mặc định");

        await db.EraGroups.Where(g => g.CurrencyId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.CurrencyId, (Guid?)null));
        db.Currencies.Remove(currency);
        await db.SaveChangesAsync();
        await logs.WriteAsync(User.GetUserId(), "CurrencyDelete", $"Xoá tiền {currency.Name} ({currency.Code})");
        return NoContent();
    }

    /// <summary>Tặng/trừ điểm một loại tiền cho người dùng (theo email) — để thử luật phát điểm.</summary>
    [HttpPost("{id:guid}/grant")]
    public async Task<ActionResult<CurrencyDto>> Grant(Guid id, GrantCurrencyRequest req)
    {
        var currency = await db.Currencies.FirstOrDefaultAsync(c => c.Id == id);
        if (currency is null) return NotFound();

        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);
        if (user is null) return Problem(statusCode: 404, title: "Không tìm thấy người dùng với email này");

        var amount = req.Amount;
        if (currency.IsDefault)
        {
            user.Coins = Math.Max(0, user.Coins + amount);
        }
        else
        {
            var row = await db.UserCurrencies.FindAsync(user.Id, currency.Id);
            if (row is null)
            {
                db.UserCurrencies.Add(new UserCurrency
                {
                    UserId = user.Id,
                    CurrencyId = currency.Id,
                    Amount = Math.Max(0, amount),
                });
            }
            else
            {
                row.Amount = Math.Max(0, row.Amount + amount);
            }
        }

        await db.SaveChangesAsync();
        await logs.WriteAsync(User.GetUserId(), "CurrencyGrant", $"{(amount >= 0 ? "Tặng" : "Trừ")} {Math.Abs(amount)} {currency.Name} cho {user.Email}");

        var balance = currency.IsDefault
            ? user.Coins
            : (await db.UserCurrencies.FindAsync(user.Id, currency.Id))?.Amount ?? 0;
        return currency.ToDto(balance);
    }

    /// <summary>Bỏ dấu tiếng Việt để mã tiền dùng được trong API.</summary>
    private static string Normalize(string? code, string name)
    {
        var raw = string.IsNullOrWhiteSpace(code) ? name : code.Trim();
        var folded = new StringBuilder();
        foreach (var c in raw.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c)) folded.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-' or '_') folded.Append('-');
        }
        var value = Regex.Replace(folded.ToString(), "-{2,}", "-").Trim('-');
        return value.Length == 0 ? "tien" : value[..Math.Min(40, value.Length)];
    }
}
