using DrinkWater.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DrinkWater.Tests.Integration;

/// <summary>Each test class gets a throwaway database on the local PostgreSQL so the
/// real schema (unique indexes, check constraints) is what gets exercised.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string AdminConnection =
        "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

    public string DatabaseName { get; } = $"drinkwater_test_{Guid.NewGuid():N}";

    private DbContextOptions<AppDbContext> Options =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"Host=localhost;Port=5432;Database={DatabaseName};Username=postgres;Password=postgres")
            .Options;

    public AppDbContext CreateContext() => new(Options);

    public async Task InitializeAsync()
    {
        await using (var admin = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                         .UseNpgsql(AdminConnection).Options))
        {
            await admin.Database.ExecuteSqlRawAsync($"CREATE DATABASE \"{DatabaseName}\"");
        }

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var admin = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(AdminConnection).Options);
        await admin.Database.ExecuteSqlRawAsync($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");
    }
}
