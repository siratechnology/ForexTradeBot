using ForexBot.Models;

namespace ForexBot.Services;

public class MetaApiMarketDataService : IMarketDataService
{
    private readonly MetaApiClient _api;
    private decimal _currentPrice;
    private readonly List<Candle> _cachedCandles = [];

    public decimal CurrentPrice => _currentPrice;

    public MetaApiMarketDataService(MetaApiClient api)
    {
        _api = api;
    }

    public async Task TickAsync()
    {
        var price = await _api.GetCurrentPriceAsync("XAUUSD");
        var ask = price.TryGetProperty("ask", out var a) ? (decimal)a.GetDouble() : _currentPrice;
        var bid = price.TryGetProperty("bid", out var b) ? (decimal)b.GetDouble() : _currentPrice;
        var mid = (ask + bid) / 2m;

        // Build a synthetic candle from previous → current price
        if (_currentPrice > 0)
        {
            var open  = _currentPrice;
            var close = mid;
            _cachedCandles.Add(new Candle(
                DateTime.UtcNow,
                open,
                Math.Max(open, close),
                Math.Min(open, close),
                close,
                100m
            ));
            // Keep last 200 candles
            if (_cachedCandles.Count > 200)
                _cachedCandles.RemoveAt(0);
        }

        _currentPrice = mid;
    }

    public List<Candle> GetHistory(string period, int count)
    {
        if (_cachedCandles.Count == 0) return [];
        count = Math.Clamp(count, 1, 50);

        if (period == "1h")
            return _cachedCandles.TakeLast(count).ToList();

        var grouped = Downsample(_cachedCandles, PeriodSize(period));
        return grouped.TakeLast(count).ToList();
    }

    public TechnicalIndicators CalculateIndicators()
    {
        var closes = _cachedCandles.Select(c => c.Close).ToList();
        if (closes.Count < 3)
            return new TechnicalIndicators(50, _currentPrice, _currentPrice, _currentPrice, _currentPrice, 0, "neutral — accumulating data");

        var rsi   = closes.Count >= 15 ? CalculateRSI(closes, 14) : 50m;
        var ma20  = closes.Count >= 20 ? closes.TakeLast(20).Average() : closes.Average();
        var ma50  = closes.Count >= 50 ? closes.TakeLast(50).Average() : closes.Average();
        var (bbU, bbL) = closes.Count >= 20
            ? CalculateBollinger(closes.TakeLast(20).ToList())
            : (_currentPrice * 1.005m, _currentPrice * 0.995m);
        var atr   = _cachedCandles.Count >= 2 ? CalculateATR(_cachedCandles.TakeLast(15).ToList(), Math.Min(14, _cachedCandles.Count - 1)) : 0m;
        var trend = ma20 > ma50 ? "bullish" : ma20 < ma50 ? "bearish" : "neutral";

        return new TechnicalIndicators(rsi, ma20, ma50, bbU, bbL, atr, trend);
    }

    private static int PeriodSize(string period) => period switch { "4h" => 4, "1d" => 24, _ => 1 };

    private static List<Candle> Downsample(List<Candle> candles, int size)
    {
        var result = new List<Candle>();
        for (int i = 0; i + size <= candles.Count; i += size)
        {
            var g = candles.Skip(i).Take(size).ToList();
            result.Add(new Candle(g[0].Timestamp, g[0].Open, g.Max(x => x.High), g.Min(x => x.Low), g[^1].Close, g.Sum(x => x.Volume)));
        }
        return result;
    }

    private static decimal CalculateRSI(List<decimal> closes, int period)
    {
        if (closes.Count < period + 1) return 50m;
        decimal gains = 0, losses = 0;
        for (int i = closes.Count - period; i < closes.Count; i++)
        {
            var diff = closes[i] - closes[i - 1];
            if (diff > 0) gains += diff; else losses -= diff;
        }
        if (losses == 0) return 100m;
        return 100m - 100m / (1m + gains / losses);
    }

    private static (decimal upper, decimal lower) CalculateBollinger(List<decimal> w)
    {
        var mean = w.Average();
        var std  = (decimal)Math.Sqrt((double)w.Select(x => (x - mean) * (x - mean)).Average());
        return (mean + 2 * std, mean - 2 * std);
    }

    private static decimal CalculateATR(List<Candle> candles, int period)
    {
        if (candles.Count < 2) return 0m;
        var trs = new List<decimal>();
        for (int i = 1; i < candles.Count; i++)
        {
            var tr = Math.Max(candles[i].High - candles[i].Low,
                     Math.Max(Math.Abs(candles[i].High - candles[i - 1].Close),
                              Math.Abs(candles[i].Low  - candles[i - 1].Close)));
            trs.Add(tr);
        }
        return trs.TakeLast(period).Average();
    }
}
