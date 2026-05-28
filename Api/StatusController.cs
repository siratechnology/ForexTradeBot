using Microsoft.AspNetCore.Mvc;

namespace ForexBot.Api;

[ApiController]
[Route("api/[controller]")]
public class StatusController : AccountController
{
    [HttpGet]
    public IActionResult Get()
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;
        var s = account.State;

        return Ok(new
        {
            label         = account.Label,
            isRunning     = s.IsRunning,
            isInitialized = s.IsInitialized,
            price         = s.CurrentPrice,
            floatPnL      = s.FloatPnL,
            hasPosition   = s.HasPosition,
            tradesToday   = s.TradesToday,
            lastCycle     = s.LastCycle,
            lastAction    = s.LastAction,
            serverTimeUtc = DateTime.UtcNow,
            settings      = s.Settings,
        });
    }

    [HttpGet("positions")]
    public async Task<IActionResult> Positions()
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;

        if (account.State.Engine == null)
            return Ok(new { positions = Array.Empty<object>(), count = 0 });

        var positions = await account.State.Engine.GetOpenPositionsAsync();
        return Ok(new { positions, count = positions.Count });
    }

    [HttpGet("trades")]
    public IActionResult Trades([FromQuery] int limit = 50)
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;

        limit = Math.Clamp(limit, 1, 100);
        var trades = account.State.RecentTrades.Take(limit).ToList();
        return Ok(new { trades, count = trades.Count });
    }
}
