using DrinkWater.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DrinkWater.Api.Data;

/// <summary>Lets `dotnet ef` build the model without starting the web host
/// (which would need a live SQL Server or the Jwt key).</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cs = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(cs))
            cs = "Host=localhost;Database=drinkwater;Username=postgres";

        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs)
            .Options);
    }
}
