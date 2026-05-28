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
        if (settings.MaxOpenPositions < 1 || settings.MaxOpenPositions > 100)
            return BadRequest(new { error = "MaxOpenPositions must be between 1 and 100" });
        if (settings.OpenPerCycle < 1 || settings.OpenPerCycle > 20)
            return BadRequest(new { error = "OpenPerCycle must be between 1 and 20" });
        if (settings.MinProfitToLock < 0m)
            return BadRequest(new { error = "MinProfitToLock must be >= 0" });
        if (settings.TrailGiveback < 0.05m || settings.TrailGiveback > 0.9m)
            return BadRequest(new { error = "TrailGiveback must be between 0.05 and 0.9" });
        if (settings.MaxLossPerTrade <= 0m)
            return BadRequest(new { error = "MaxLossPerTrade must be > 0" });
        if (settings.CycleSeconds < 1 || settings.CycleSeconds > 3600)
            return BadRequest(new { error = "CycleSeconds must be between 1 and 3600" });

        account.State.Settings.LotSize          = settings.LotSize;
        account.State.Settings.MaxTradesPerDay  = settings.MaxTradesPerDay;
        account.State.Settings.TradingStartUtc  = settings.TradingStartUtc;
        account.State.Settings.TradingEndUtc    = settings.TradingEndUtc;
        account.State.Settings.MaxOpenPositions = settings.MaxOpenPositions;
        account.State.Settings.OpenPerCycle     = settings.OpenPerCycle;
        account.State.Settings.MinProfitToLock  = settings.MinProfitToLock;
        account.State.Settings.TrailGiveback    = settings.TrailGiveback;
        account.State.Settings.MaxLossPerTrade  = settings.MaxLossPerTrade;
        account.State.Settings.CycleSeconds     = settings.CycleSeconds;

        await _registry.PersistSettingsAsync();

        return Ok(new { message = "Settings updated", settings = account.State.Settings });
    }
}
