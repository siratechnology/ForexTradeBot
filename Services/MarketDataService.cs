using ForexBot.Models;

namespace ForexBot.Services;

public class MarketDataService : IMarketDataService
{
    private const decimal StartPrice = 3350m;
    private const decimal HourlyStdDev = 12m;
    private const decimal MinPrice = 2800m;
    private const decimal MaxPrice = 4500m;

    private readonly Random _rng = new();
    private readonly List<Candle> _hourlyCandles = [];
    private DateTime _currentTime = DateTime.UtcNow.Date;
    private decimal _currentPrice = StartPrice;

    public decimal CurrentPrice => _currentPrice;

    public MarketDataService()
    {
        // Seed 200 hours of historical candles
        for (int i = 200; i >= 1; i--)
            GenerateCandle(_currentTime.AddHours(-i));
    }

    public void Tick()
    {
        _currentTime = _currentTime.AddHours(1);
        GenerateCandle(_currentTime);
    }

    public Task TickAsync() { Tick(); return Task.CompletedTask; }

    private void GenerateCandle(DateTime timestamp)
    {
        var open = _currentPrice;
        var change = (decimal)GaussianSample() * HourlyStdDev;
        var close = Math.Clamp(open + change, MinPrice, MaxPrice);
        var high = Math.Max(open, close) + Math.Abs((decimal)GaussianSample() * HourlyStdDev * 0.3m);
        var low  = Math.Min(open, close) - Math.Abs((decimal)GaussianSample() * HourlyStdDev * 0.3m);
        var volume = (decimal)(_rng.NextDouble() * 500 + 100);

        _currentPrice = close;
        _hourlyCandles.Add(new Candle(timestamp, open, high, low, close, volume));
    }

    private double GaussianSample()
    {
        // Box-Muller transform
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = 1.0 - _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    public List<Candle> GetHistory(string period, int count)
    {
        var hourly = _hourlyCandles.TakeLast(count * PeriodMultiplier(period)).ToList();
        if (period == "1h") return hourly.TakeLast(count).ToList();
        return Downsample(hourly, PeriodMultiplier(period)).TakeLast(count).ToList();
    }

    private static int PeriodMultiplier(string period) => period switch
    {
        "4h" => 4,
        "1d" => 24,
        _    => 1,
    };

    private static List<Candle> Downsample(List<Candle> candles, int groupSize)
    {
        var result = new List<Candle>();
        for (int i = 0; i + groupSize <= candles.Count; i += groupSize)
        {
            var group = candles.Skip(i).Take(groupSize).ToList();
            result.Add(new Candle(
                group[0].Timestamp,
                group[0].Open,
                group.Max(c => c.High),
                group.Min(c => c.Low),
                group[^1].Close,
                group.Sum(c => c.Volume)
            ));
        }
        return result;
    }

    public TechnicalIndicators CalculateIndicators()
    {
        var closes = _hourlyCandles.TakeLast(55).Select(c => c.Close).ToList();
        if (closes.Count < 20)
            return new TechnicalIndicators(50, _currentPrice, _currentPrice, _currentPrice, _currentPrice, 0, "neutral");

        var rsi   = CalculateRSI(closes, 14);
        var ma20  = closes.TakeLast(20).Average();
        var ma50  = closes.Count >= 50 ? closes.TakeLast(50).Average() : closes.Average();
        var (bbUpper, bbLower) = CalculateBollinger(closes.TakeLast(20).ToList());
        var atr   = CalculateATR(_hourlyCandles.TakeLast(15).ToList(), 14);

        string trend = ma20 > ma50 ? "bullish" : ma20 < ma50 ? "bearish" : "neutral";

        return new TechnicalIndicators(rsi, ma20, ma50, bbUpper, bbLower, atr, trend);
    }

    private static decimal CalculateRSI(List<decimal> closes, int period)
    {
        if (closes.Count < period + 1) return 50m;

        decimal gains = 0, losses = 0;
        for (int i = closes.Count - period; i < closes.Count; i++)
        {
            var diff = closes[i] - closes[i - 1];
            if (diff > 0) gains  += diff;
            else          losses -= diff;
        }

        if (losses == 0) return 100m;
        var rs = gains / losses;
        return 100m - 100m / (1m + rs);
    }

    private static (decimal upper, decimal lower) CalculateBollinger(List<decimal> window)
    {
        var mean   = window.Average();
        var stdDev = (decimal)Math.Sqrt((double)window.Select(x => (x - mean) * (x - mean)).Average());
        return (mean + 2 * stdDev, mean - 2 * stdDev);
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
