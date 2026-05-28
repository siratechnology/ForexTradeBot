using ForexBot.Models;

namespace ForexBot.Services;

public class BotSettings
{
    public decimal LotSize { get; set; } = 0.01m;
    public int MaxTradesPerDay { get; set; } = 0;      // 0 = unlimited
    public string? TradingStartUtc { get; set; }       // "HH:mm" or null = 24h
    public string? TradingEndUtc { get; set; }

    // ── Multi-position scalper tuning ───────────────────────────────────────────
    public int MaxOpenPositions { get; set; } = 10;    // max positions held at once
    public int OpenPerCycle { get; set; } = 1;         // new positions opened per cycle (scale-in rate)
    public decimal MinProfitToLock { get; set; } = 1.5m; // $ profit before trailing exit arms
    public decimal TrailGiveback { get; set; } = 0.4m;   // close when profit retraces this fraction of its peak
    public decimal MaxLossPerTrade { get; set; } = 3.0m; // hard $ stop per position
    public int CycleSeconds { get; set; } = 5;           // loop interval (rate at which positions open)
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
