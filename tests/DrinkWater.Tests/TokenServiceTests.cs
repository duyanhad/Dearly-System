using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.IdentityModel.Tokens;

namespace DrinkWater.Tests;

public class TokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "DrinkWater",
        Audience = "DrinkWaterClient",
        Key = "unit-test-key-that-is-long-enough-32-chars",
        ExpireHours = 12
    };

    [Fact]
    public void Token_carries_user_id_email_role_and_expiry()
    {
        var user = new User
        {
            Email = "a@b.com",
            FullName = "Nguyen Van A",
            Role = Roles.Admin,
            PasswordHash = "x"
        };
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(Create(user, now));

        Assert.Equal(user.Id.ToString(), jwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("a@b.com", jwt.Claims.First(c => c.Type == ClaimTypes.Email).Value);
        Assert.Equal(Roles.Admin, jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal(now.AddHours(12), jwt.ValidTo);
    }

    [Fact]
    public void Token_signed_with_another_key_is_rejected()
    {
        var user = new User { Email = "a@b.com", FullName = "A", PasswordHash = "x" };
        var raw = Create(user, DateTime.UtcNow);

        Assert.ThrowsAny<Exception>(() => new JwtSecurityTokenHandler().ValidateToken(
            raw,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Options.Issuer,
                ValidateAudience = true,
                ValidAudience = Options.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes("another-key-that-is-long-enough-32-characters")),
                ValidateLifetime = false
            },
            out _));
    }

    private static string Create(User user, DateTime nowUtc) =>
        new TokenService(Microsoft.Extensions.Options.Options.Create(Options)).Create(user, nowUtc);
}
