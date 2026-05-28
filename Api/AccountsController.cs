using ForexBot.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForexBot.Api;

[ApiController]
[Route("api/[controller]")]
public class AccountsController : ControllerBase
{
    private readonly AccountRegistry _registry;
    public AccountsController(AccountRegistry registry) => _registry = registry;

    // GET /api/accounts  (admin only)
    [HttpGet]
    public IActionResult List()
    {
        if (!HttpContext.IsAdmin())
            return StatusCode(403, new { error = "Admin key required (ADMIN_KEY env var)" });

        return Ok(new { accounts = _registry.GetAllSummaries() });
    }

    // POST /api/accounts  (admin only)
    // Body: { "label": "Alice Live", "metaToken": "...", "metaAccountId": "...", "apiKey": "optional" }
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (!HttpContext.IsAdmin())
            return StatusCode(403, new { error = "Admin key required (ADMIN_KEY env var)" });

        if (string.IsNullOrWhiteSpace(req.Label))
            return BadRequest(new { error = "label is required" });

        try
        {
            var entry = await _registry.RegisterAsync(req.Label, req.MetaToken, req.MetaAccountId, req.ApiKey);
            return Ok(new
            {
                message      = "Account registered — bot starting in background",
                apiKey       = entry.ApiKey,
                label        = entry.Label,
                registeredAt = entry.RegisteredAt,
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // DELETE /api/accounts/{apiKey}  (admin only)
    [HttpDelete("{apiKey}")]
    public async Task<IActionResult> Remove(string apiKey)
    {
        if (!HttpContext.IsAdmin())
            return StatusCode(403, new { error = "Admin key required (ADMIN_KEY env var)" });

        var removed = await _registry.RemoveAsync(apiKey);
        if (!removed) return NotFound(new { error = $"Account '{apiKey}' not found" });

        return Ok(new { message = $"Account '{apiKey}' removed and bot stopped" });
    }
}
