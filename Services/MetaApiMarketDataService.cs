using System.Text.Json;
using ForexBot.Models;

namespace ForexBot.Services;

public class MetaApiMarketDataService : IMarketDataService
{
    private readonly MetaApiClient _api;
    private decimal _currentPrice;

    private readonly List<Candle> _history1h = [];
    private readonly List<Candle> _history4h = [];
    private readonly List<Candle> _history1d = [];

    public decimal CurrentPrice => _currentPrice;

    public MetaApiMarketDataService(MetaApiClient api)
    {
        _api = api;
    }

    public async Task InitializeAsync()
    {
        Console.WriteLine("  Loading historical candles...");
        try
        {
            _history1h.AddRange((await _api.GetCandlesAsync("XAUUSD", "1h", 200)).Select(ParseMetaCandle));
            _history4h.AddRange((await _api.GetCandlesAsync("XAUUSD", "4h", 100)).Select(ParseMetaCandle));
            _history1d.AddRange((await _api.GetCandlesAsync("XAUUSD", "1d",  50)).Select(ParseMetaCandle));
            Console.WriteLine($"  MetaAPI seeded: {_history1h.Count}×1h, {_history4h.Count}×4h, {_history1d.Count}×1d candles");
        }
        catch
        {
            Console.WriteLine("  MetaAPI historical data unavailable — falling back to Yahoo Finance...");
            await LoadFromYahooAsync();
        }
    }

    private async Task LoadFromYahooAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        try
        {
            // ~1 month of hourly bars (Gold Futures GC=F — price tracks spot XAUUSD closely)
            var raw1h = await http.GetStringAsync(
                "https://query1.finance.yahoo.com/v8/finance/chart/GC=F?interval=1h&range=1mo");
            _history1h.AddRange(ParseYahooCandles(raw1h).TakeLast(200));

            // 3 months of daily bars
            var raw1d = await http.GetStringAsync(
                "https://query1.finance.yahoo.com/v8/finance/chart/GC=F?interval=1d&range=3mo");
            _history1d.AddRange(ParseYahooCandles(raw1d).TakeLast(50));

            // 4h: downsample from 1h groups
            _history4h.AddRange(Downsample(_history1h, 4).TakeLast(100));

            Console.WriteLine($"  Yahoo Finance seeded: {_history1h.Count}×1h, {_history4h.Count}×4h, {_history1d.Count}×1d candles");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Yahoo Finance fallback failed: {ex.Message}");
            Console.WriteLine("  Bot will warm up from live ticks — indicators active after ~50 cycles (~4h)");
        }
    }

    public async Task TickAsync()
    {
        var price = await _api.GetCurrentPriceAsync("XAUUSD");
        var ask = price.TryGetProperty("ask", out var a) ? (decimal)a.GetDouble() : _currentPrice;
        var bid = price.TryGetProperty("bid", out var b) ? (decimal)b.GetDouble() : _currentPrice;
        var mid = (ask + bid) / 2m;

        if (_currentPrice > 0)
        {
            var open  = _currentPrice;
            var close = mid;
            _history1h.Add(new Candle(DateTime.UtcNow, open, Math.Max(open, close), Math.Min(open, close), close, 100m));
            if (_history1h.Count > 500) _history1h.RemoveAt(0);
        }

        _currentPrice = mid;
    }

    public List<Candle> GetHistory(string period, int count)
    {
        count = Math.Clamp(count, 1, 50);
        return period switch
        {
            "4h" => _history4h.TakeLast(count).ToList(),
            "1d" => _history1d.TakeLast(count).ToList(),
            _    => _history1h.TakeLast(count).ToList(),
        };
    }

    public TechnicalIndicators CalculateIndicators()
    {
        var closes = _history1h.Select(c => c.Close).ToList();
        if (closes.Count < 3)
            return new TechnicalIndicators(50, _currentPrice, _currentPrice, _currentPrice, _currentPrice, 0, "neutral — accumulating data");

        var rsi        = closes.Count >= 15 ? CalculateRSI(closes, 14) : 50m;
        var ma20       = closes.Count >= 20 ? closes.TakeLast(20).Average() : closes.Average();
        var ma50       = closes.Count >= 50 ? closes.TakeLast(50).Average() : closes.Average();
        var (bbU, bbL) = closes.Count >= 20
            ? CalculateBollinger(closes.TakeLast(20).ToList())
            : (_currentPrice * 1.005m, _currentPrice * 0.995m);
        var atr        = _history1h.Count >= 2
            ? CalculateATR(_history1h.TakeLast(15).ToList(), Math.Min(14, _history1h.Count - 1))
            : 0m;
        var trend      = ma20 > ma50 ? "bullish" : ma20 < ma50 ? "bearish" : "neutral";

        return new TechnicalIndicators(rsi, ma20, ma50, bbU, bbL, atr, trend);
    }

    private static Candle ParseMetaCandle(JsonElement el)
    {
        var time  = el.TryGetProperty("time",       out var t) ? DateTime.Parse(t.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind) : DateTime.UtcNow;
        var open  = el.TryGetProperty("open",       out var o) ? (decimal)o.GetDouble() : 0m;
        var high  = el.TryGetProperty("high",       out var h) ? (decimal)h.GetDouble() : 0m;
        var low   = el.TryGetProperty("low",        out var l) ? (decimal)l.GetDouble() : 0m;
        var close = el.TryGetProperty("close",      out var c) ? (decimal)c.GetDouble() : 0m;
        var vol   = el.TryGetProperty("tickVolume", out var v) ? (decimal)v.GetDouble() : 0m;
        return new Candle(time, open, high, low, close, vol);
    }

    private static List<Candle> ParseYahooCandles(string json)
    {
        using var doc    = JsonDocument.Parse(json);
        var result       = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
        var timestamps   = result.GetProperty("timestamp").EnumerateArray().ToList();
        var quote        = result.GetProperty("indicators").GetProperty("quote")[0];
        var opens        = quote.GetProperty("open").EnumerateArray().ToList();
        var highs        = quote.GetProperty("high").EnumerateArray().ToList();
        var lows         = quote.GetProperty("low").EnumerateArray().ToList();
        var closes       = quote.GetProperty("close").EnumerateArray().ToList();
        var volumes      = quote.GetProperty("volume").EnumerateArray().ToList();

        var candles = new List<Candle>();
        for (int i = 0; i < timestamps.Count; i++)
        {
            if (i >= closes.Count || closes[i].ValueKind == JsonValueKind.Null) continue;
            candles.Add(new Candle(
                DateTimeOffset.FromUnixTimeSeconds(timestamps[i].GetInt64()).UtcDateTime,
                i < opens.Count   && opens[i].ValueKind   != JsonValueKind.Null ? (decimal)opens[i].GetDouble()   : 0m,
                i < highs.Count   && highs[i].ValueKind   != JsonValueKind.Null ? (decimal)highs[i].GetDouble()   : 0m,
                i < lows.Count    && lows[i].ValueKind    != JsonValueKind.Null ? (decimal)lows[i].GetDouble()    : 0m,
                (decimal)closes[i].GetDouble(),
                i < volumes.Count && volumes[i].ValueKind != JsonValueKind.Null ? (decimal)volumes[i].GetDouble() : 0m
            ));
        }
        return candles;
    }

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
