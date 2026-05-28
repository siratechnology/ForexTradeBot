using ForexBot.Services;

namespace ForexBot.Api;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, AccountRegistry registry)
    {
        if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var providedKey) || string.IsNullOrWhiteSpace(providedKey))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Missing X-Api-Key header" });
            return;
        }

        var key      = providedKey.ToString();
        var adminKey = Environment.GetEnvironmentVariable("ADMIN_KEY");

        // Admin key — grants access to /api/accounts management endpoints
        if (!string.IsNullOrWhiteSpace(adminKey) && key == adminKey)
        {
            ctx.Items["IsAdmin"] = true;
            await _next(ctx);
            return;
        }

        // No ADMIN_KEY set → admin endpoints are open (dev/bootstrap mode)
        if (string.IsNullOrWhiteSpace(adminKey) && ctx.Request.Path.StartsWithSegments("/api/accounts"))
        {
            ctx.Items["IsAdmin"] = true;
            await _next(ctx);
            return;
        }

        // Resolve user account by API key
        var account = registry.TryGet(key);
        if (account == null)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        ctx.Items["Account"] = account;
        await _next(ctx);
    }
}
