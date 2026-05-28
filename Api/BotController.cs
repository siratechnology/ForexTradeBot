using Microsoft.AspNetCore.Mvc;

namespace ForexBot.Api;

[ApiController]
[Route("api/[controller]")]
public class BotController : AccountController
{
    [HttpPost("start")]
    public IActionResult Start()
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;

        account.State.IsRunning  = true;
        account.State.LastAction = "Bot started via API";
        return Ok(new { status = "running", message = "Bot is now active", label = account.Label });
    }

    [HttpPost("stop")]
    public IActionResult Stop()
    {
        var err = RequireAccount(out var account);
        if (err != null) return err;

        account.State.IsRunning  = false;
        account.State.LastAction = "Bot stopped via API";
        return Ok(new { status = "stopped", message = "Bot paused — price tracking continues", label = account.Label });
    }
}
