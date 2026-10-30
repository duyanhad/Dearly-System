using DrinkWater.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DrinkWater.Api.Middleware;

/// <summary>Global exception handler: one log row per unhandled exception, one ProblemDetails
/// response to the client. Detailed messages only outside Production.</summary>
public sealed class ExceptionLoggingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionLoggingMiddleware> logger,
    IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext context, SystemLogWriter writer)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await writer.WriteAsync(
                context.User.Identity?.IsAuthenticated == true
                    ? Guid.TryParse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null
                    : null,
                "ServerError",
                $"{context.Request.Method} {context.Request.Path} — {ex.GetType().Name}: {ex.Message}",
                Entities.LogLevels.Error);

            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = 500,
                Title = "Lỗi máy chủ",
                Detail = env.IsDevelopment() ? ex.Message : "Đã xảy ra lỗi, vui lòng thử lại."
            });
        }
    }
}
