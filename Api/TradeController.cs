using ForexBot.Models;
using Microsoft.AspNetCore.Mvc;

namespace ForexBot.Api;

public record TradeRequest(decimal Lots);

[ApiController]
[Route("api/[controller]")]
public class TradeController : AccountController
{
    [HttpPost("buy")]
    public async Task<IActionResult> Buy([FromBody] TradeRequest req)
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;
        if (account.State.Engine == null) return StatusCode(503, new { error = "Bot not initialized yet" });
        if (req.Lots < 0.01m) return BadRequest(new { error = "Minimum lot size is 0.01" });

        var result = await account.State.Engine.BuyAsync(account.State.CurrentPrice, req.Lots, "Manual BUY via API");
        account.State.AddTrade(new TradeRecord(DateTime.UtcNow, "BUY", req.Lots, account.State.CurrentPrice, "Manual via API"));
        account.State.LastAction = $"Manual BUY {req.Lots} lot @ ${account.State.CurrentPrice:F2}";
        return Ok(new { result, price = account.State.CurrentPrice });
    }

    [HttpPost("sell")]
    public async Task<IActionResult> Sell([FromBody] TradeRequest req)
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;
        if (account.State.Engine == null) return StatusCode(503, new { error = "Bot not initialized yet" });
        if (req.Lots < 0.01m) return BadRequest(new { error = "Minimum lot size is 0.01" });

        var result = await account.State.Engine.SellAsync(account.State.CurrentPrice, req.Lots, "Manual SELL via API");
        account.State.AddTrade(new TradeRecord(DateTime.UtcNow, "SELL", req.Lots, account.State.CurrentPrice, "Manual via API"));
        account.State.LastAction = $"Manual SELL {req.Lots} lot @ ${account.State.CurrentPrice:F2}";
        return Ok(new { result, price = account.State.CurrentPrice });
    }

    [HttpPost("close")]
    public async Task<IActionResult> Close()
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;
        if (account.State.Engine == null) return StatusCode(503, new { error = "Bot not initialized yet" });

        var result = await account.State.Engine.CloseAllAsync("Manual close via API");
        account.State.LastAction = "Manual CLOSE ALL via API";
        return Ok(new { result });
    }
}
