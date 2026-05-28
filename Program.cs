using Anthropic;
using ForexBot.Services;

// ── API key check ──────────────────────────────────────────────────────────
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
{
    Console.Error.WriteLine("Error: ANTHROPIC_API_KEY is not set.");
    Console.Error.WriteLine("Set it with: $env:ANTHROPIC_API_KEY = 'sk-ant-...'");
    Environment.Exit(1);
}

int cycles          = args.Length > 0 && int.TryParse(args[0], out var n) ? n : int.MaxValue;
int intervalSeconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 300;

// ── Detect mode ────────────────────────────────────────────────────────────
var metaToken     = Environment.GetEnvironmentVariable("META_API_TOKEN");
var metaAccountId = Environment.GetEnvironmentVariable("META_ACCOUNT_ID");

bool isLiveMode = !string.IsNullOrWhiteSpace(metaToken) && !string.IsNullOrWhiteSpace(metaAccountId);

Console.WriteLine("=== XAUUSD Gold Trading Bot ===");
Console.WriteLine($"Model  : claude-opus-4-7");
Console.WriteLine($"Mode   : {(isLiveMode ? "LIVE — MetaAPI + MT5" : "Paper Trading (Simulation)")}");
Console.WriteLine($"Cycles  : {(cycles == int.MaxValue ? "∞ (run forever)" : cycles.ToString())}");
Console.WriteLine($"Interval: {intervalSeconds}s");
Console.WriteLine("================================\n");

var anthropicClient = new AnthropicClient();

if (isLiveMode)
{
    // ── Live MT5 mode via MetaAPI ──────────────────────────────────────────
    using var metaApi = new MetaApiClient(metaToken!, metaAccountId!);

    await metaApi.InitializeAsync();
    await metaApi.WaitForDeployedAsync();

    var market = new MetaApiMarketDataService(metaApi);
    await market.InitializeAsync();
    var engine = new MetaApiTradingEngine(metaApi);
    var bot    = new GoldTradingBot(anthropicClient, market, engine);

    for (int i = 1; i <= cycles; i++)
    {
        await bot.AnalyzeAndTradeAsync(i);
        if (i < cycles) await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
    }

    Console.WriteLine();
    Console.WriteLine(await engine.GetPortfolioStatusAsync(market.CurrentPrice));
}
else
{
    // ── Paper trading mode (simulation) ───────────────────────────────────
    Console.WriteLine("Tip: Set META_API_TOKEN and META_ACCOUNT_ID to trade on a real MT5 account.\n");

    var market = new MarketDataService();
    var engine = new TradingEngine();
    var bot    = new GoldTradingBot(anthropicClient, market, engine);

    for (int i = 1; i <= cycles; i++)
    {
        await bot.AnalyzeAndTradeAsync(i);
        if (i < cycles) await Task.Delay(2000);
    }

    Console.WriteLine();
    Console.WriteLine(await engine.GetPortfolioStatusAsync(market.CurrentPrice));
}
