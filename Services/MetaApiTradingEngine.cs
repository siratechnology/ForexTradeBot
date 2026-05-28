using ForexBot.Models;

namespace ForexBot.Services;

public class MetaApiTradingEngine : ITradingEngine
{
    private readonly MetaApiClient _api;
    private const string Symbol = "XAUUSD";

    public MetaApiTradingEngine(MetaApiClient api)
    {
        _api = api;
    }

    // Opens an independent position. Buy and sell coexist — neither closes the other.
    // NOTE: simultaneous opposite positions require a HEDGING MT5 account; a netting
    // account will still merge them at the broker (an account-type setting, not code).
    public async Task<string> OpenAsync(TradeType side, decimal price, decimal lots, string reason)
    {
        lots = Math.Max(0.01m, Math.Round(lots, 2));
        var actionType = side == TradeType.Buy ? "ORDER_TYPE_BUY" : "ORDER_TYPE_SELL";
        var label      = side == TradeType.Buy ? "BUY" : "SELL";

        var result = await _api.PlaceTradeAsync(new
        {
            actionType,
            symbol  = Symbol,
            volume  = (double)lots,
            comment = reason[..Math.Min(reason.Length, 31)],
        });

        var code = result.TryGetProperty("stringCode", out var sc) ? sc.GetString() : "UNKNOWN";
        if (code == "TRADE_RETCODE_DONE")
            return $"{label} FILLED: {lots} lot(s) XAUUSD @ ~${price:F2} | Reason: {reason}";

        var msg = result.TryGetProperty("message", out var m) ? m.GetString() : result.ToString();
        return $"{label} FAILED ({code}): {msg}";
    }

    public async Task<string> ClosePositionAsync(string positionId, string reason)
    {
        var result = await _api.PlaceTradeAsync(new
        {
            actionType = "POSITION_CLOSE_ID",
            positionId,
            comment    = reason[..Math.Min(reason.Length, 31)],
        });
        var code = result.TryGetProperty("stringCode", out var sc) ? sc.GetString() : "UNKNOWN";
        if (code == "TRADE_RETCODE_DONE")
            return $"CLOSED position {positionId} | {reason}";

        var msg = result.TryGetProperty("message", out var m) ? m.GetString() : result.ToString();
        return $"CLOSE FAILED ({code}) for {positionId}: {msg}";
    }

    // Manual API buy/sell open independent positions (no netting), same as the bot.
    public Task<string> BuyAsync(decimal price, decimal lots, string reason)  => OpenAsync(TradeType.Buy,  price, lots, reason);
    public Task<string> SellAsync(decimal price, decimal lots, string reason) => OpenAsync(TradeType.Sell, price, lots, reason);

    public async Task<(decimal floatPnL, bool hasPosition)> GetPositionStatusAsync(decimal currentPrice)
    {
        var positions = await _api.GetPositionsAsync();
        var xau = positions.Where(p =>
            p.TryGetProperty("symbol", out var s) && s.GetString() == Symbol).ToList();
        var pnl = xau.Sum(p => p.TryGetProperty("profit", out var pr) ? pr.GetDecimal() : 0m);
        return (pnl, xau.Count > 0);
    }

    public async Task<string> CloseAllAsync(string reason)
    {
        var positions = await _api.GetPositionsAsync();
        var xau = positions.Where(p =>
            p.TryGetProperty("symbol", out var s) && s.GetString() == Symbol).ToList();
        if (xau.Count == 0) return "No positions to close.";

        var results = new List<string>();
        foreach (var pos in xau)
        {
            var posId = pos.GetProperty("id").GetString();
            var r     = await _api.PlaceTradeAsync(new
            {
                actionType = "POSITION_CLOSE_ID",
                positionId = posId,
                comment    = reason[..Math.Min(reason.Length, 31)],
            });
            var code = r.TryGetProperty("stringCode", out var sc) ? sc.GetString() : "UNKNOWN";
            results.Add($"{posId}: {code}");
        }
        return $"CLOSED {xau.Count} position(s) — {string.Join(", ", results)}";
    }

    public async Task<List<OpenPosition>> GetOpenPositionsAsync()
    {
        var positions = await _api.GetPositionsAsync();
        return positions
            .Where(p => p.TryGetProperty("symbol", out var s) && s.GetString() == Symbol)
            .Select(p =>
            {
                var id    = p.TryGetProperty("id",        out var idP) ? idP.GetString() ?? ""   : "";
                var type  = p.TryGetProperty("type",      out var t)   ? (t.GetString() == "POSITION_TYPE_BUY" ? "BUY" : "SELL") : "?";
                var lots  = p.TryGetProperty("volume",    out var v)   ? v.GetDecimal()           : 0m;
                var open  = p.TryGetProperty("openPrice", out var op)  ? op.GetDecimal()          : 0m;
                var pnl   = p.TryGetProperty("profit",    out var pr)  ? pr.GetDecimal()          : 0m;
                return new OpenPosition(id, type, lots, open, pnl);
            })
            .ToList();
    }

    public async Task<string> GetPortfolioStatusAsync(decimal currentPrice)
    {
        var info = await _api.GetAccountInfoAsync();
        var balance    = info.TryGetProperty("balance",    out var bal)  ? bal.GetDecimal()  : 0m;
        var equity     = info.TryGetProperty("equity",     out var eq)   ? eq.GetDecimal()   : 0m;
        var freeMargin = info.TryGetProperty("freeMargin", out var fm)   ? fm.GetDecimal()   : 0m;
        var currency   = info.TryGetProperty("currency",   out var cur)  ? cur.GetString()   : "USD";
        var pnl        = equity - balance;

        var positions = await _api.GetPositionsAsync();
        var xauPositions = positions.Where(p =>
            p.TryGetProperty("symbol", out var s) && s.GetString() == Symbol).ToList();

        var posLines = xauPositions.Count == 0
            ? "  (no open positions)"
            : string.Join("\n", xauPositions.Select(p =>
            {
                var type    = p.TryGetProperty("type",       out var t)  ? t.GetString()   : "?";
                var vol     = p.TryGetProperty("volume",     out var v)  ? v.GetDecimal()  : 0m;
                var opPrice = p.TryGetProperty("openPrice",  out var op) ? op.GetDecimal() : 0m;
                var profit  = p.TryGetProperty("profit",     out var pr) ? pr.GetDecimal() : 0m;
                return $"  {type} {vol} lot @ ${opPrice:F2} | P&L: ${profit:F2}";
            }));

        return $"""
            === LIVE MT5 PORTFOLIO ({currency}) ===
            Balance    : {balance:F2}
            Equity     : {equity:F2}
            Free Margin: {freeMargin:F2}
            Float P&L  : {pnl:+0.00;-0.00}
            Gold Price : ${currentPrice:F2}/oz
            Positions  :
            {posLines}
            =========================================
            """;
    }
}
