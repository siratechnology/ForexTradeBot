using ForexBot.Services;
using Microsoft.AspNetCore.Mvc;

namespace ForexBot.Api;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : AccountController
{
    private readonly AccountRegistry _registry;
    public SettingsController(AccountRegistry registry) => _registry = registry;

    [HttpGet]
    public IActionResult Get()
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;
        return Ok(account.State.Settings);
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] BotSettings settings)
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;

        if (settings.LotSize < 0.01m || settings.LotSize > 100m)
            return BadRequest(new { error = "LotSize must be between 0.01 and 100" });
        if (settings.MaxTradesPerDay < 0)
            return BadRequest(new { error = "MaxTradesPerDay must be >= 0" });

        account.State.Settings.LotSize         = settings.LotSize;
        account.State.Settings.MaxTradesPerDay = settings.MaxTradesPerDay;
        account.State.Settings.TradingStartUtc = settings.TradingStartUtc;
        account.State.Settings.TradingEndUtc   = settings.TradingEndUtc;

        await _registry.PersistSettingsAsync();

        return Ok(new { message = "Settings updated", settings = account.State.Settings });
    }
}
