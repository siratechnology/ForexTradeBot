using ForexBot.Models;

namespace ForexBot.Services;

public class RuleBasedBot
{
    private readonly IMarketDataService _market;
    private readonly ITradingEngine     _engine;

    // ── Strategy parameters ───────────────────────────────────────────────────
    private const decimal TakeProfit = 3m;    // Close position when profit >= $3
    private const decimal StopLoss   = -8m;   // Close position when loss  <= -$8
    private const decimal LotSize    = 0.01m; // 0.01 lot = 1 oz XAUUSD

    private const decimal RsiBuyThreshold  = 40m; // Buy  when RSI falls below this
    private const decimal RsiSellThreshold = 60m; // Sell when RSI rises above this

    public RuleBasedBot(IMarketDataService market, ITradingEngine engine)
    {
        _market = market;
        _engine = engine;
    }

    public async Task RunCycleAsync(int cycle)
    {
        await _market.TickAsync();
        var price = _market.CurrentPrice;
        Console.WriteLine($"\n--- Cycle {cycle} | XAUUSD: ${price:F2} | {DateTime.UtcNow:HH:mm:ss} UTC ---");

        var (floatPnL, hasPosition) = await _engine.GetPositionStatusAsync(price);

        // ── Step 1: manage open position ─────────────────────────────────────
        if (hasPosition)
        {
            Console.WriteLine($"  [POSITION]  Float P&L: ${floatPnL:+0.00;-0.00}");

            if (floatPnL >= TakeProfit)
            {
                var result = await _engine.CloseAllAsync($"TP +${floatPnL:F2}");
                Console.WriteLine($"  [TAKE PROFIT +${floatPnL:F2}]  {result}");
                return;
            }

            if (floatPnL <= StopLoss)
            {
                var result = await _engine.CloseAllAsync($"SL {floatPnL:F2}");
                Console.WriteLine($"  [STOP LOSS  {floatPnL:F2}]  {result}");
                return;
            }

            Console.WriteLine($"  [HOLD]  TP at +${TakeProfit} | SL at ${StopLoss} | current ${floatPnL:+0.00;-0.00}");
            return;
        }

        // ── Step 2: look for entry signal ────────────────────────────────────
        var ind = _market.CalculateIndicators();
        Console.WriteLine($"  RSI={ind.RSI:F1}  Price=${price:F2}  MA20=${ind.MA20:F2}  " +
                          $"BB[{ind.BollingerLower:F2}–{ind.BollingerUpper:F2}]  ATR=${ind.ATR:F2}  Trend={ind.Trend}");

        var nearLower = price <= ind.BollingerLower * 1.001m; // within 0.1% of lower band
        var nearUpper = price >= ind.BollingerUpper * 0.999m; // within 0.1% of upper band

        if (ind.RSI < RsiBuyThreshold && price < ind.MA20)
        {
            var reason = $"RSI={ind.RSI:F1} oversold, below MA20";
            var result = await _engine.BuyAsync(price, LotSize, reason);
            Console.WriteLine($"  [BUY]  {result}");
        }
        else if (ind.RSI < RsiBuyThreshold && nearLower)
        {
            var reason = $"RSI={ind.RSI:F1} oversold, near lower BB";
            var result = await _engine.BuyAsync(price, LotSize, reason);
            Console.WriteLine($"  [BUY]  {result}");
        }
        else if (ind.RSI > RsiSellThreshold && price > ind.MA20)
        {
            var reason = $"RSI={ind.RSI:F1} overbought, above MA20";
            var result = await _engine.SellAsync(price, LotSize, reason);
            Console.WriteLine($"  [SELL] {result}");
        }
        else if (ind.RSI > RsiSellThreshold && nearUpper)
        {
            var reason = $"RSI={ind.RSI:F1} overbought, near upper BB";
            var result = await _engine.SellAsync(price, LotSize, reason);
            Console.WriteLine($"  [SELL] {result}");
        }
        else
        {
            Console.WriteLine($"  [WAIT]  RSI {ind.RSI:F1} in neutral zone ({RsiBuyThreshold}–{RsiSellThreshold}) — no signal");
        }
    }
}
