using ForexBot.Services;

int cycles          = args.Length > 0 && int.TryParse(args[0], out var n) ? n : int.MaxValue;
int intervalSeconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 60;

var metaToken     = Environment.GetEnvironmentVariable("META_API_TOKEN");
var metaAccountId = Environment.GetEnvironmentVariable("META_ACCOUNT_ID");
bool isLiveMode   = !string.IsNullOrWhiteSpace(metaToken) && !string.IsNullOrWhiteSpace(metaAccountId);

Console.WriteLine("=== XAUUSD Gold Scalping Bot ===");
Console.WriteLine($"Mode    : {(isLiveMode ? "LIVE — MetaAPI + MT5" : "Paper Trading (Simulation)")}");
Console.WriteLine($"Cycles  : {(cycles == int.MaxValue ? "∞ (run forever)" : cycles.ToString())}");
Console.WriteLine($"Interval: {intervalSeconds}s");
Console.WriteLine($"Strategy: RSI + Bollinger | TP=$3 | SL=$8 | 0.01 lot");
Console.WriteLine("================================\n");

if (isLiveMode)
{
    using var metaApi = new MetaApiClient(metaToken!, metaAccountId!);
    await metaApi.InitializeAsync();
    await metaApi.WaitForDeployedAsync();

    var market = new MetaApiMarketDataService(metaApi);
    await market.InitializeAsync();

    var engine = new MetaApiTradingEngine(metaApi);
    var bot    = new RuleBasedBot(market, engine);

    for (int i = 1; i <= cycles; i++)
    {
        await bot.RunCycleAsync(i);
        if (i < cycles) await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
    }

    Console.WriteLine();
    Console.WriteLine(await engine.GetPortfolioStatusAsync(market.CurrentPrice));
}
else
{
    Console.WriteLine("Tip: Set META_API_TOKEN and META_ACCOUNT_ID to trade on a real MT5 account.\n");

    var market = new MarketDataService();
    var engine = new TradingEngine();
    var bot    = new RuleBasedBot(market, engine);

    for (int i = 1; i <= cycles; i++)
    {
        await bot.RunCycleAsync(i);
        if (i < cycles) await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
    }

    Console.WriteLine();
    Console.WriteLine(await engine.GetPortfolioStatusAsync(market.CurrentPrice));
}
