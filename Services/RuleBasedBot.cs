using ForexBot.Models;

namespace ForexBot.Services;

public class RuleBasedBot
{
    private readonly IMarketDataService _market;
    private readonly ITradingEngine     _engine;
    private readonly BotStateService?   _state;

    private const decimal TakeProfit        = 3m;
    private const decimal StopLoss          = -8m;
    private const decimal DefaultLotSize    = 0.01m;
    private const decimal RsiBuyThreshold   = 40m;
    private const decimal RsiSellThreshold  = 60m;

    private decimal LotSize => _state?.Settings.LotSize ?? DefaultLotSize;

    public RuleBasedBot(IMarketDataService market, ITradingEngine engine, BotStateService? state = null)
    {
        _market = market;
        _engine = engine;
        _state  = state;
    }

    public async Task RunCycleAsync(int cycle)
    {
        await _market.TickAsync();
        var price = _market.CurrentPrice;

        if (_state != null) { _state.CurrentPrice = price; _state.LastCycle = DateTime.UtcNow; }

        Console.WriteLine($"\n--- Cycle {cycle} | XAUUSD: ${price:F2} | {DateTime.UtcNow:HH:mm:ss} UTC ---");

        var (floatPnL, hasPosition) = await _engine.GetPositionStatusAsync(price);

        if (_state != null) { _state.FloatPnL = floatPnL; _state.HasPosition = hasPosition; }

        if (hasPosition)
        {
            Console.WriteLine($"  [POSITION]  Float P&L: ${floatPnL:+0.00;-0.00}");

            if (floatPnL >= TakeProfit)
            {
                var result = await _engine.CloseAllAsync($"TP +${floatPnL:F2}");
                Console.WriteLine($"  [TAKE PROFIT +${floatPnL:F2}]  {result}");
                _state?.AddTrade(new TradeRecord(DateTime.UtcNow, "CLOSE", LotSize, price, $"TP +${floatPnL:F2}"));
                if (_state != null) { _state.LastAction = $"TAKE PROFIT +${floatPnL:F2}"; _state.HasPosition = false; }
                return;
            }

            if (floatPnL <= StopLoss)
            {
                var result = await _engine.CloseAllAsync($"SL {floatPnL:F2}");
                Console.WriteLine($"  [STOP LOSS  {floatPnL:F2}]  {result}");
                _state?.AddTrade(new TradeRecord(DateTime.UtcNow, "CLOSE", LotSize, price, $"SL {floatPnL:F2}"));
                if (_state != null) { _state.LastAction = $"STOP LOSS {floatPnL:F2}"; _state.HasPosition = false; }
                return;
            }

            if (_state != null) _state.LastAction = $"HOLD | P&L ${floatPnL:+0.00;-0.00}";
            Console.WriteLine($"  [HOLD]  TP at +${TakeProfit} | SL at ${StopLoss} | current ${floatPnL:+0.00;-0.00}");
            return;
        }

        var ind = _market.CalculateIndicators();
        Console.WriteLine($"  RSI={ind.RSI:F1}  Price=${price:F2}  MA20=${ind.MA20:F2}  " +
                          $"BB[{ind.BollingerLower:F2}–{ind.BollingerUpper:F2}]  ATR=${ind.ATR:F2}  Trend={ind.Trend}");

        var nearLower = price <= ind.BollingerLower * 1.001m;
        var nearUpper = price >= ind.BollingerUpper * 0.999m;
        var lots = LotSize;

        if (ind.RSI < RsiBuyThreshold && price < ind.MA20)
        {
            var reason = $"RSI={ind.RSI:F1} oversold, below MA20";
            var result = await _engine.BuyAsync(price, lots, reason);
            Console.WriteLine($"  [BUY]  {result}");
            _state?.AddTrade(new TradeRecord(DateTime.UtcNow, "BUY", lots, price, reason));
            if (_state != null) _state.LastAction = $"BUY {lots} lot @ ${price:F2}";
        }
        else if (ind.RSI < RsiBuyThreshold && nearLower)
        {
            var reason = $"RSI={ind.RSI:F1} oversold, near lower BB";
            var result = await _engine.BuyAsync(price, lots, reason);
            Console.WriteLine($"  [BUY]  {result}");
            _state?.AddTrade(new TradeRecord(DateTime.UtcNow, "BUY", lots, price, reason));
            if (_state != null) _state.LastAction = $"BUY {lots} lot @ ${price:F2}";
        }
        else if (ind.RSI > RsiSellThreshold && price > ind.MA20)
        {
            var reason = $"RSI={ind.RSI:F1} overbought, above MA20";
            var result = await _engine.SellAsync(price, lots, reason);
            Console.WriteLine($"  [SELL] {result}");
            _state?.AddTrade(new TradeRecord(DateTime.UtcNow, "SELL", lots, price, reason));
            if (_state != null) _state.LastAction = $"SELL {lots} lot @ ${price:F2}";
        }
        else if (ind.RSI > RsiSellThreshold && nearUpper)
        {
            var reason = $"RSI={ind.RSI:F1} overbought, near upper BB";
            var result = await _engine.SellAsync(price, lots, reason);
            Console.WriteLine($"  [SELL] {result}");
            _state?.AddTrade(new TradeRecord(DateTime.UtcNow, "SELL", lots, price, reason));
            if (_state != null) _state.LastAction = $"SELL {lots} lot @ ${price:F2}";
        }
        else
        {
            if (_state != null) _state.LastAction = $"WAIT | RSI={ind.RSI:F1}";
            Console.WriteLine($"  [WAIT]  RSI {ind.RSI:F1} in neutral zone ({RsiBuyThreshold}–{RsiSellThreshold}) — no signal");
        }
    }
}
