using DrinkWater.Api.Data;
using DrinkWater.Api.Dtos;
using DrinkWater.Api.Entities;
using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrinkWater.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    TokenService tokens,
    IClock clock,
    SystemLogWriter logs,
    IOptions<JwtOptions> jwt,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
            return Problem(statusCode: 400, title: "Email đã được sử dụng");

        var user = new User
        {
            Email = email,
            FullName = req.FullName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            CreatedAt = clock.UtcNow
        };
        db.Users.Add(user);
        db.CupPresets.AddRange(
            new CupPreset { UserId = user.Id, Name = "Cốc thủy tinh", VolumeMl = 250 },
            new CupPreset { UserId = user.Id, Name = "Bình thể thao", VolumeMl = 500 });
        await db.SaveChangesAsync();

        logger.LogInformation("User registered {UserId}", user.Id);
        return StatusCode(201, BuildAuth(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        {
            await logs.WriteAsync(user?.Id, "Login", $"Email={email}, kết quả=sai mật khẩu",
                LogLevels.Warning);
            return Problem(statusCode: 401, title: "Email hoặc mật khẩu không đúng");
        }

        if (user.IsLocked)
        {
            await logs.WriteAsync(user.Id, "Login", $"Email={email}, kết quả=tài khoản bị khóa",
                LogLevels.Warning);
            return Problem(statusCode: 403, title: "Tài khoản đã bị khóa");
        }

        await logs.WriteAsync(user.Id, "Login", $"Email={email}, kết quả=thành công");
        return BuildAuth(user);
    }

    /// <summary>Đăng xuất: đổi SecurityStamp để mọi token đã phát của tài khoản này hết hiệu lực.</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var user = await db.Users.FindAsync(User.GetUserId());
        if (user is null) return NoContent();

        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Đổi mật khẩu tự phục vụ (dùng ở trang đăng nhập): phải biết mật khẩu hiện tại.</summary>
    [HttpPost("change-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
        {
            await logs.WriteAsync(user?.Id, "ChangePassword", $"Email={email}, kết quả=sai mật khẩu hiện tại",
                LogLevels.Warning);
            return Problem(statusCode: 400, title: "Email hoặc mật khẩu hiện tại không đúng");
        }

        if (user.IsLocked)
            return Problem(statusCode: 403, title: "Tài khoản đã bị khoá");

        if (BCrypt.Net.BCrypt.Verify(req.NewPassword, user.PasswordHash))
            return Problem(statusCode: 400, title: "Mật khẩu mới phải khác mật khẩu hiện tại");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        // Đổi mật khẩu phải đăng xuất mọi thiết bị khác.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();

        await logs.WriteAsync(user.Id, "ChangePassword", $"Email={email}, kết quả=thành công");
        return NoContent();
    }

    private AuthResponse BuildAuth(User user)
    {
        var now = clock.UtcNow;
        var token = tokens.Create(user, now);
        return new AuthResponse(token, now.AddHours(jwt.Value.ExpireHours), user.ToProfile());
    }
}
