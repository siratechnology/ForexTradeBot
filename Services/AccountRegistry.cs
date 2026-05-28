using System.Text.Json;

namespace ForexBot.Services;

public class AccountRegistry : IHostedService
{
    private readonly Dictionary<string, AccountEntry> _accounts = new();
    private readonly string _dataFile;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AccountRegistry()
    {
        _dataFile = Path.Combine(AppContext.BaseDirectory, "accounts.json");
    }

    public async Task StartAsync(CancellationToken ct)
    {
        // Load persisted accounts
        if (File.Exists(_dataFile))
        {
            try
            {
                var json    = await File.ReadAllTextAsync(_dataFile, ct);
                var saved   = JsonSerializer.Deserialize<List<PersistedAccount>>(json) ?? [];
                foreach (var p in saved)
                {
                    var entry = BuildEntry(p.ApiKey, p.Label, p.MetaToken, p.MetaAccountId, p.RegisteredAt);
                    entry.State.Settings.LotSize          = p.LotSize;
                    entry.State.Settings.MaxTradesPerDay  = p.MaxTradesPerDay;
                    entry.State.Settings.TradingStartUtc  = p.TradingStartUtc;
                    entry.State.Settings.TradingEndUtc    = p.TradingEndUtc;
                    entry.State.Settings.MaxOpenPositions = p.MaxOpenPositions;
                    entry.State.Settings.OpenPerCycle     = p.OpenPerCycle;
                    entry.State.Settings.MinProfitToLock  = p.MinProfitToLock;
                    entry.State.Settings.TrailGiveback    = p.TrailGiveback;
                    entry.State.Settings.MaxLossPerTrade  = p.MaxLossPerTrade;
                    entry.State.Settings.CycleSeconds     = p.CycleSeconds;
                    _accounts[entry.ApiKey] = entry;
                    StartBotTask(entry);
                }
                Console.WriteLine($"  Loaded {_accounts.Count} account(s) from accounts.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: could not load accounts.json — {ex.Message}");
            }
        }

        // Backward-compat: auto-register from env vars if accounts.json was empty
        var metaToken     = Environment.GetEnvironmentVariable("META_API_TOKEN");
        var metaAccountId = Environment.GetEnvironmentVariable("META_ACCOUNT_ID");
        var apiKey        = Environment.GetEnvironmentVariable("API_KEY");

        if (!_accounts.Any() && !string.IsNullOrWhiteSpace(apiKey))
        {
            bool isLive = !string.IsNullOrWhiteSpace(metaToken) && !string.IsNullOrWhiteSpace(metaAccountId);
            await RegisterAsync(
                label:         isLive ? "Default (MetaAPI)" : "Default (Paper Trading)",
                metaToken:     metaToken ?? "",
                metaAccountId: metaAccountId ?? "",
                customApiKey:  apiKey);
            Console.WriteLine($"  Auto-registered default account from env vars (API key: {apiKey})");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        List<AccountEntry> entries;
        lock (_lock) entries = _accounts.Values.ToList();

        foreach (var a in entries) a.Cts.Cancel();

        var tasks = entries.Where(a => a.BotTask != null).Select(a => a.BotTask!).ToArray();
        if (tasks.Length > 0)
        {
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* ignored on shutdown */ }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public AccountEntry? TryGet(string apiKey)
    {
        lock (_lock) return _accounts.TryGetValue(apiKey, out var a) ? a : null;
    }

    public IReadOnlyList<AccountSummary> GetAllSummaries()
    {
        lock (_lock) return _accounts.Values.Select(ToSummary).ToList();
    }

    public async Task<AccountEntry> RegisterAsync(
        string label, string metaToken, string metaAccountId, string? customApiKey = null)
    {
        var key   = string.IsNullOrWhiteSpace(customApiKey) ? GenerateKey() : customApiKey!;
        var entry = BuildEntry(key, label, metaToken, metaAccountId, DateTime.UtcNow);
        lock (_lock)
        {
            if (_accounts.ContainsKey(key))
                throw new InvalidOperationException($"API key '{key}' already exists.");
            _accounts[key] = entry;
        }
        StartBotTask(entry);
        await SaveAsync();
        return entry;
    }

    // Persist current settings for all accounts (call after settings change)
    public Task PersistSettingsAsync() => SaveAsync();

    public async Task<bool> RemoveAsync(string apiKey)
    {
        AccountEntry? entry;
        lock (_lock)
        {
            if (!_accounts.TryGetValue(apiKey, out entry)) return false;
            _accounts.Remove(apiKey);
        }
        entry.Cts.Cancel();
        await SaveAsync();
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AccountEntry BuildEntry(
        string key, string label, string metaToken, string metaAccountId, DateTime registeredAt)
    {
        return new AccountEntry
        {
            ApiKey        = key,
            Label         = label,
            MetaToken     = metaToken,
            MetaAccountId = metaAccountId,
            RegisteredAt  = registeredAt,
        };
    }

    private static void StartBotTask(AccountEntry entry)
    {
        var runner = new AccountBotRunner(entry.MetaToken, entry.MetaAccountId, entry.State);
        entry.BotTask = Task.Run(() => runner.RunAsync(entry.Cts.Token));
    }

    private async Task SaveAsync()
    {
        List<PersistedAccount> list;
        lock (_lock)
        {
            list = _accounts.Values.Select(a => new PersistedAccount
            {
                ApiKey         = a.ApiKey,
                Label          = a.Label,
                MetaToken      = a.MetaToken,
                MetaAccountId  = a.MetaAccountId,
                RegisteredAt   = a.RegisteredAt,
                LotSize          = a.State.Settings.LotSize,
                MaxTradesPerDay  = a.State.Settings.MaxTradesPerDay,
                TradingStartUtc  = a.State.Settings.TradingStartUtc,
                TradingEndUtc    = a.State.Settings.TradingEndUtc,
                MaxOpenPositions = a.State.Settings.MaxOpenPositions,
                OpenPerCycle     = a.State.Settings.OpenPerCycle,
                MinProfitToLock  = a.State.Settings.MinProfitToLock,
                TrailGiveback    = a.State.Settings.TrailGiveback,
                MaxLossPerTrade  = a.State.Settings.MaxLossPerTrade,
                CycleSeconds     = a.State.Settings.CycleSeconds,
            }).ToList();
        }
        await File.WriteAllTextAsync(_dataFile, JsonSerializer.Serialize(list, JsonOpts));
    }

    private static string GenerateKey() => Guid.NewGuid().ToString("N")[..20];

    private static AccountSummary ToSummary(AccountEntry a) => new()
    {
        ApiKey        = a.ApiKey,
        Label         = a.Label,
        IsRunning     = a.State.IsRunning,
        IsInitialized = a.State.IsInitialized,
        Price         = a.State.CurrentPrice,
        FloatPnL      = a.State.FloatPnL,
        HasPosition   = a.State.HasPosition,
        TradesToday   = a.State.TradesToday,
        LastAction    = a.State.LastAction,
        RegisteredAt  = a.RegisteredAt,
    };
}
