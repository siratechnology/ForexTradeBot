using ForexBot.Models;

namespace ForexBot.Services;

// Runs the trading loop for one account. Created and managed by AccountRegistry.
public class AccountBotRunner
{
    private readonly string           _metaToken;
    private readonly string           _metaAccountId;
    private readonly BotStateService  _state;

    public AccountBotRunner(string metaToken, string metaAccountId, BotStateService state)
    {
        _metaToken     = metaToken;
        _metaAccountId = metaAccountId;
        _state         = state;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        bool isLive = !string.IsNullOrWhiteSpace(_metaToken) && !string.IsNullOrWhiteSpace(_metaAccountId);
        _state.LastAction = isLive ? "Initializing MetaAPI..." : "Starting paper trading...";

        MetaApiClient? metaApi = null;
        try
        {
            IMarketDataService market;
            ITradingEngine engine;

            if (isLive)
            {
                metaApi = new MetaApiClient(_metaToken, _metaAccountId);
                await metaApi.InitializeAsync();
                await metaApi.WaitForDeployedAsync();

                var metaMarket = new MetaApiMarketDataService(metaApi);
                await metaMarket.InitializeAsync();
                market = metaMarket;
                engine = new MetaApiTradingEngine(metaApi);
            }
            else
            {
                market = new MarketDataService();
                engine = new TradingEngine();
            }

            _state.Market        = market;
            _state.Engine        = engine;
            _state.IsInitialized = true;
            _state.LastAction    = "Ready — waiting for first cycle";

            var bot   = new RuleBasedBot(market, engine, _state);
            int cycle = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _state.CheckDailyReset();

                    if (_state.CanTrade())
                    {
                        cycle++;
                        await bot.RunCycleAsync(cycle);
                    }
                    else
                    {
                        await market.TickAsync();
                        _state.CurrentPrice = market.CurrentPrice;
                        (decimal pnl, bool hasPos) = await engine.GetPositionStatusAsync(market.CurrentPrice);
                        _state.FloatPnL     = pnl;
                        _state.HasPosition  = hasPos;
                        _state.LastCycle    = DateTime.UtcNow;

                        if (!_state.IsRunning)
                            _state.LastAction = "Bot paused via API";
                        else if (_state.Settings.MaxTradesPerDay > 0 && _state.TradesToday >= _state.Settings.MaxTradesPerDay)
                            _state.LastAction = $"Daily limit reached ({_state.TradesToday}/{_state.Settings.MaxTradesPerDay})";
                        else
                            _state.LastAction = "Outside trading hours";
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Console.WriteLine($"  [Bot error] {ex.Message}");
                    _state.LastAction = $"Error: {ex.Message[..Math.Min(ex.Message.Length, 80)]}";
                }

                var interval = int.TryParse(Environment.GetEnvironmentVariable("BOT_INTERVAL_SECONDS"), out var s) ? s : 60;
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"  [Bot init failed] {ex.Message}");
            _state.LastAction = $"Init failed: {ex.Message}";
        }
        finally
        {
            metaApi?.Dispose();
        }
    }
}
