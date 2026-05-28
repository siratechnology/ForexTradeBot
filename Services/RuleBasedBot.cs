using ForexBot.Models;

namespace ForexBot.Services;

// Multi-position scalper:
//  • opens many INDEPENDENT positions (buys and sells never net each other),
//  • takes profit dynamically — a winner stays open while it keeps gaining and is closed
//    only once it gives back part of its peak profit (trailing), and
//  • caps each position with a hard stop loss.
// All thresholds live in BotSettings so the iOS app can tune them live.
public class RuleBasedBot
{
    private readonly IMarketDataService _market;
    private readonly ITradingEngine     _engine;
    private readonly BotStateService?   _state;

    private const decimal DefaultLotSize   = 0.01m;
    private const decimal RsiBuyThreshold  = 40m;
    private const decimal RsiSellThreshold = 60m;

    // Best floating profit each open position has reached, keyed by position id — lets the
    // trailing exit detect when a winner starts handing profit back.
    private readonly Dictionary<string, decimal> _peakPnl = new();

    public RuleBasedBot(IMarketDataService market, ITradingEngine engine, BotStateService? state = null)
    {
        _market = market;
        _engine = engine;
        _state  = state;
    }

    private decimal LotSize         => _state?.Settings.LotSize          ?? DefaultLotSize;
    private int     MaxOpen         => _state?.Settings.MaxOpenPositions ?? 10;
    private int     OpenPerCycle    => _state?.Settings.OpenPerCycle     ?? 1;
    private decimal MinProfitToLock => _state?.Settings.MinProfitToLock  ?? 1.5m;
    private decimal TrailGiveback   => _state?.Settings.TrailGiveback    ?? 0.4m;
    private decimal MaxLossPerTrade => _state?.Settings.MaxLossPerTrade  ?? 3.0m;

    // Full cycle: manage exits on existing positions, then open new ones if there's a signal.
    // The caller ticks the market and updates state.CurrentPrice before calling this.
    public async Task RunCycleAsync(int cycle, decimal price)
    {
        Console.WriteLine($"\n--- Cycle {cycle} | XAUUSD: ${price:F2} | {DateTime.UtcNow:HH:mm:ss} UTC ---");

        var openCount = await ManageExitsAsync(price);

        var ind       = _market.CalculateIndicators();
        var nearLower = price <= ind.BollingerLower * 1.001m;
        var nearUpper = price >= ind.BollingerUpper * 0.999m;

        var buySignal  = ind.RSI < RsiBuyThreshold  && (price < ind.MA20 || nearLower);
        var sellSignal = ind.RSI > RsiSellThreshold && (price > ind.MA20 || nearUpper);

        Console.WriteLine($"  RSI={ind.RSI:F1}  Price=${price:F2}  MA20=${ind.MA20:F2}  " +
                          $"BB[{ind.BollingerLower:F2}-{ind.BollingerUpper:F2}]  open={openCount}/{MaxOpen}");

        if (!buySignal && !sellSignal)
        {
            if (_state != null) _state.LastAction = $"WAIT | RSI={ind.RSI:F1} | open {openCount}";
            Console.WriteLine($"  [WAIT]  RSI {ind.RSI:F1} in neutral zone — no signal");
            return;
        }

        if (openCount >= MaxOpen)
        {
            if (_state != null) _state.LastAction = $"MAX OPEN {openCount}/{MaxOpen} | trailing exits";
            Console.WriteLine($"  [SKIP]  at max open positions ({openCount}/{MaxOpen})");
            return;
        }

        var side   = buySignal ? TradeType.Buy : TradeType.Sell;
        var label  = buySignal ? "BUY" : "SELL";
        var toOpen = Math.Min(OpenPerCycle, MaxOpen - openCount);
        var reason = $"RSI={ind.RSI:F1} {(buySignal ? "oversold" : "overbought")}";

        for (int i = 0; i < toOpen; i++)
        {
            var result = await _engine.OpenAsync(side, price, LotSize, reason);
            Console.WriteLine($"  [{label}]  {result}");
            _state?.AddTrade(new TradeRecord(DateTime.UtcNow, label, LotSize, price, reason));
        }
        if (_state != null) _state.LastAction = $"{label} x{toOpen} @ ${price:F2} | open {openCount + toOpen}";
    }

    // Exit pass over every open position: trailing profit-lock + hard stop loss.
    // Returns how many positions remain open. Safe to call even when entries are paused,
    // so positions stay protected outside trading hours or at the daily limit.
    public async Task<int> ManageExitsAsync(decimal price)
    {
        var (floatPnL, _) = await _engine.GetPositionStatusAsync(price);
        var positions     = await _engine.GetOpenPositionsAsync();

        // Forget peaks for positions that no longer exist.
        var liveIds = positions.Select(p => p.Id).ToHashSet();
        foreach (var stale in _peakPnl.Keys.Where(k => !liveIds.Contains(k)).ToList())
            _peakPnl.Remove(stale);

        int     remaining      = positions.Count;
        decimal remainingFloat = floatPnL;

        foreach (var pos in positions)
        {
            var peak = Math.Max(_peakPnl.GetValueOrDefault(pos.Id, pos.PnL), pos.PnL);
            _peakPnl[pos.Id] = peak;

            string? closeReason = null;
            if (pos.PnL <= -MaxLossPerTrade)
                closeReason = $"SL ${pos.PnL:F2}";
            else if (peak >= MinProfitToLock && pos.PnL <= peak * (1m - TrailGiveback))
                closeReason = $"TRAIL +${pos.PnL:F2} (peak +${peak:F2})";

            if (closeReason != null)
            {
                var result = await _engine.ClosePositionAsync(pos.Id, closeReason);
                Console.WriteLine($"  [CLOSE {pos.Type}]  {result}");
                _state?.AddTrade(new TradeRecord(DateTime.UtcNow, "CLOSE", pos.Lots, price, closeReason));
                _peakPnl.Remove(pos.Id);
                remaining--;
                remainingFloat -= pos.PnL;
            }
        }

        if (_state != null)
        {
            _state.FloatPnL    = remainingFloat;
            _state.HasPosition = remaining > 0;
        }
        return remaining;
    }
}
