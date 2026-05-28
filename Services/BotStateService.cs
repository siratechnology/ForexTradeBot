using ForexBot.Models;

namespace ForexBot.Services;

public class BotSettings
{
    public decimal LotSize { get; set; } = 0.01m;
    public int MaxTradesPerDay { get; set; } = 0;      // 0 = unlimited
    public string? TradingStartUtc { get; set; }       // "HH:mm" or null = 24h
    public string? TradingEndUtc { get; set; }
}

public class BotStateService
{
    private readonly List<TradeRecord> _recentTrades = new();
    private DateTime _lastReset = DateTime.UtcNow.Date;
    private readonly object _lock = new();

    public bool IsRunning { get; set; } = true;
    public BotSettings Settings { get; } = new();

    // Updated each cycle by BotBackgroundService
    public decimal CurrentPrice { get; set; }
    public decimal FloatPnL { get; set; }
    public bool HasPosition { get; set; }
    public int TradesToday { get; set; }
    public DateTime LastCycle { get; set; }
    public string LastAction { get; set; } = "Starting...";
    public bool IsInitialized { get; set; }

    // Shared engine/market references set by background service after init
    public ITradingEngine? Engine { get; set; }
    public IMarketDataService? Market { get; set; }

    public IReadOnlyList<TradeRecord> RecentTrades
    {
        get { lock (_lock) return _recentTrades.ToList(); }
    }

    public void AddTrade(TradeRecord trade)
    {
        lock (_lock)
        {
            _recentTrades.Insert(0, trade);
            if (_recentTrades.Count > 100) _recentTrades.RemoveAt(100);
            TradesToday++;
        }
    }

    public void CheckDailyReset()
    {
        var today = DateTime.UtcNow.Date;
        if (today > _lastReset) { _lastReset = today; TradesToday = 0; }
    }

    public bool CanTrade()
    {
        if (!IsRunning || !IsInitialized) return false;
        if (Settings.MaxTradesPerDay > 0 && TradesToday >= Settings.MaxTradesPerDay) return false;
        if (Settings.TradingStartUtc != null && Settings.TradingEndUtc != null)
        {
            if (!TimeOnly.TryParse(Settings.TradingStartUtc, out var s)) return true;
            if (!TimeOnly.TryParse(Settings.TradingEndUtc, out var e)) return true;
            var now = TimeOnly.FromDateTime(DateTime.UtcNow);
            return now >= s && now <= e;
        }
        return true;
    }
}
