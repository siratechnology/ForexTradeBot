using ForexBot.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForexBot.Api;

public static class HttpContextExtensions
{
    public static AccountEntry? GetCurrentAccount(this HttpContext ctx) =>
        ctx.Items.TryGetValue("Account", out var a) ? a as AccountEntry : null;

    public static bool IsAdmin(this HttpContext ctx) =>
        ctx.Items.TryGetValue("IsAdmin", out var v) && v is true;
}

// Base controller that resolves the current account or returns 401/503
public abstract class AccountController : ControllerBase
{
    protected AccountEntry? CurrentAccount => HttpContext.GetCurrentAccount();

    protected IActionResult RequireAccount(out AccountEntry account)
    {
        account = CurrentAccount!;
        if (account == null) return Unauthorized(new { error = "No account resolved for this API key" });
        return null!;
    }
}
