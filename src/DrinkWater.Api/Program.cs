using System.Security.Claims;
using System.Text;
using DrinkWater.Api.Data;
using DrinkWater.Api.Hubs;
using DrinkWater.Api.Middleware;
using DrinkWater.Api.Services;
using DrinkWater.Api.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
var rembgOptions = builder.Configuration.GetSection("Rembg").Get<RembgOptions>() ?? new RembgOptions();
builder.Services.Configure<RembgOptions>(builder.Configuration.GetSection("Rembg"));
builder.Services.AddHttpClient<RembgClient>(c => c.Timeout = TimeSpan.FromSeconds(rembgOptions.TimeoutSeconds));
builder.Services.AddSingleton<IClock, AppClock>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<WaterService>();
builder.Services.AddScoped<RewardService>();
builder.Services.AddScoped<SocialService>();
builder.Services.AddScoped<SystemLogWriter>();
builder.Services.AddScoped<IImageStorage, LocalDiskStorage>();
builder.Services.AddSingleton<IPushSender, LogPushSender>();
builder.Services.Configure<WebPushOptions>(builder.Configuration.GetSection("WebPush"));
builder.Services.AddScoped<IWebPushSender, WebPushSender>();
builder.Services.AddHostedService<ReminderService>();

builder.Services.AddSignalR();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Dearly API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Dán JWT từ /api/auth/login"
    });
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", doc)] = new List<string>()
    });
});

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Thiếu cấu hình Jwt");
if (jwt.Key.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key phải dài tối thiểu 32 ký tự. Đặt trong appsettings.Development.json hoặc biến môi trường Jwt__Key.");
// Khoá mẫu từng nằm trong repo → từ chối chạy để không ai vô tình deploy với khoá đã lộ.
if (jwt.Key is "dev-only-signing-key-change-me-32-chars-minimum")
    throw new InvalidOperationException(
        "Jwt:Key đang dùng khoá mẫu đã công khai. Hãy tạo khoá mới (>= 32 ký tự) trong .env hoặc biến môi trường.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        // SignalR can only pass the token in the query string on WebSocket connects.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            },
            // Kiểm tra trạng thái người dùng mỗi request: token đã phát không còn giá trị khi
            // tài khoản bị khoá, bị đổi quyền, hoặc đã đăng xuất (SecurityStamp đổi).
            OnTokenValidated = async ctx =>
            {
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var idText = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(idText, out var userId))
                {
                    ctx.Fail("Token không hợp lệ");
                    return;
                }

                var state = await db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.IsLocked, u.Role, u.SecurityStamp })
                    .FirstOrDefaultAsync();

                if (state is null) { ctx.Fail("Tài khoản không tồn tại"); return; }
                if (state.IsLocked) { ctx.Fail("Tài khoản đã bị khoá"); return; }

                var stamp = ctx.Principal!.FindFirstValue(TokenService.StampClaim);
                if (!string.Equals(stamp, state.SecurityStamp, StringComparison.Ordinal))
                {
                    ctx.Fail("Phiên đã hết hiệu lực, đăng nhập lại");
                    return;
                }

                if (!string.Equals(ctx.Principal!.FindFirstValue(ClaimTypes.Role), state.Role, StringComparison.Ordinal))
                    ctx.Fail("Quyền đã thay đổi, đăng nhập lại");
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddPolicy("spa", p =>
{
    // Web (dev) + app Capacitor: Android dùng http://localhost, iOS dùng capacitor://localhost.
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? ["http://localhost:5173", "http://127.0.0.1:5173"];
    var allowed = new HashSet<string>(
        origins.Concat([
            "capacitor://localhost", "https://localhost", "http://localhost",
            "http://localhost:4173", "http://127.0.0.1:4173",
            "http://localhost:5173", "http://127.0.0.1:5173"
        ]),
        StringComparer.OrdinalIgnoreCase);
    // Giai đoạn test: chấp nhận thêm dev/preview mở bằng IP mạng LAN và tunnel trycloudflare (HTTPS tạm).
    p.SetIsOriginAllowed(origin =>
            allowed.Contains(origin)
            || origin.StartsWith("http://192.168.", StringComparison.Ordinal)
            || origin.EndsWith(".trycloudflare.com", StringComparison.Ordinal)
            || origin.EndsWith(".vercel.app", StringComparison.Ordinal))
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
}));

builder.Services.AddProblemDetails();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Migration chạy ở MỌI môi trường để host (Render/VPS) tự dựng schema lần đầu.
await MigrateOnStartAsync(app);

// Ảnh upload + file tải về (APK) nằm trong wwwroot. .apk không có trong bảng MIME mặc định
// nên phải khai báo, nếu không sẽ trả 404.
var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypes.Mappings[".apk"] = "application/vnd.android.package-archive";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });
app.UseCors("spa");
app.UseMiddleware<ExceptionLoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

static async Task MigrateOnStartAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        await EraSeeder.SeedErasAsync(db, scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("EraSeed"));
        await EraSeeder.SeedDefaultGroupAsync(db, scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("EraSeed"));
        await DbSeeder.SeedAdminAsync(db,
            scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>(),
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Seed"));
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex,
            "Không kết nối được PostgreSQL nên bỏ qua migrate. Sửa ConnectionStrings:Default rồi chạy lại.");
    }
}

public partial class Program;
