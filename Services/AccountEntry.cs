using System.Text.Json.Serialization;

namespace ForexBot.Services;

public class AccountEntry
{
    public string ApiKey        { get; init; } = "";
    public string Label         { get; init; } = "";
    public string MetaToken     { get; init; } = "";        // never exposed via API
    public string MetaAccountId { get; init; } = "";        // never exposed via API
    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;

    // Per-account bot state (shared between bot task and API controllers)
    public BotStateService State { get; } = new();

    [JsonIgnore] public Task? BotTask { get; set; }
    [JsonIgnore] public CancellationTokenSource Cts { get; } = new();
}

// Serialised to accounts.json — tokens stored, summaries never expose them
public class PersistedAccount
{
    public string ApiKey         { get; set; } = "";
    public string Label          { get; set; } = "";
    public string MetaToken      { get; set; } = "";
    public string MetaAccountId  { get; set; } = "";
    public DateTime RegisteredAt { get; set; }
    public decimal LotSize           { get; set; } = 0.01m;
    public int     MaxTradesPerDay   { get; set; } = 0;
    public string? TradingStartUtc   { get; set; }
    public string? TradingEndUtc     { get; set; }

    // Multi-position scalper tuning
    public int     MaxOpenPositions  { get; set; } = 10;
    public int     OpenPerCycle      { get; set; } = 1;
    public decimal MinProfitToLock   { get; set; } = 1.5m;
    public decimal TrailGiveback     { get; set; } = 0.4m;
    public decimal MaxLossPerTrade   { get; set; } = 3.0m;
    public int     CycleSeconds      { get; set; } = 5;
}

public class RegisterRequest
{
    public string  Label         { get; set; } = "";
    public string  MetaToken     { get; set; } = "";
    public string  MetaAccountId { get; set; } = "";
    public string? ApiKey        { get; set; }   // optional custom key; auto-generated if null
}

public class AccountSummary
{
    public string   ApiKey        { get; set; } = "";
    public string   Label         { get; set; } = "";
    public bool     IsRunning     { get; set; }
    public bool     IsInitialized { get; set; }
    public decimal  Price         { get; set; }
    public decimal  FloatPnL      { get; set; }
    public bool     HasPosition   { get; set; }
    public int      TradesToday   { get; set; }
    public string   LastAction    { get; set; } = "";
    public DateTime RegisteredAt  { get; set; }
}
