using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Data;

public static class DbSeeder
{
    /// <summary>Creates the one admin the portal needs. Runs only when nobody has the Admin role.</summary>
    public static async Task SeedAdminAsync(AppDbContext db, IOptions<AppOptions> options, ILogger logger)
    {
        var seed = options.Value.SeedAdmin;
        if (string.IsNullOrWhiteSpace(seed.Password))
        {
            logger.LogWarning("Bỏ qua seed admin: chưa đặt App:SeedAdmin:Password");
            return;
        }

        if (await db.Users.AnyAsync(u => u.Role == Roles.Admin))
            return;

        var email = seed.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            logger.LogWarning("Bỏ qua seed admin: email {Email} đã tồn tại với role khác", email);
            return;
        }

        db.Users.Add(new User
        {
            Email = email,
            FullName = seed.FullName,
            Role = Roles.Admin,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(seed.Password)
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Đã tạo admin {Email}", email);
    }
}
