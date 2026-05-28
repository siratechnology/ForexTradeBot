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

    public async Task<string> BuyAsync(decimal price, decimal lots, string reason)
    {
        lots = Math.Max(0.01m, Math.Round(lots, 2));

        var result = await _api.PlaceTradeAsync(new
        {
            actionType = "ORDER_TYPE_BUY",
            symbol     = Symbol,
            volume     = (double)lots,
            comment    = reason[..Math.Min(reason.Length, 31)],
        });

        var code = result.TryGetProperty("stringCode", out var sc) ? sc.GetString() : "UNKNOWN";
        if (code == "TRADE_RETCODE_DONE")
            return $"BUY FILLED: {lots} lot(s) XAUUSD @ ~${price:F2} | Reason: {reason}";

        var msg = result.TryGetProperty("message", out var m) ? m.GetString() : result.ToString();
        return $"BUY FAILED ({code}): {msg}";
    }

    public async Task<string> SellAsync(decimal price, decimal lots, string reason)
    {
        // Close all open buy positions first
        var positions = await _api.GetPositionsAsync();
        var buyPositions = positions
            .Where(p => p.TryGetProperty("type", out var t) && t.GetString() == "POSITION_TYPE_BUY"
                     && p.TryGetProperty("symbol", out var s) && s.GetString() == Symbol)
            .ToList();

        if (buyPositions.Count > 0)
        {
            var results = new List<string>();
            foreach (var pos in buyPositions)
            {
                var posId = pos.GetProperty("id").GetString();
                var closeResult = await _api.PlaceTradeAsync(new
                {
                    actionType = "POSITION_CLOSE_ID",
                    positionId = posId,
                    comment    = reason[..Math.Min(reason.Length, 31)],
                });
                var code = closeResult.TryGetProperty("stringCode", out var sc) ? sc.GetString() : "UNKNOWN";
                results.Add($"Position {posId}: {code}");
            }
            return $"CLOSED {buyPositions.Count} buy position(s) | {string.Join(", ", results)}";
        }

        // If no buy positions, open a sell position
        lots = Math.Max(0.01m, Math.Round(lots, 2));
        var sellResult = await _api.PlaceTradeAsync(new
        {
            actionType = "ORDER_TYPE_SELL",
            symbol     = Symbol,
            volume     = (double)lots,
            comment    = reason[..Math.Min(reason.Length, 31)],
        });

        var sellCode = sellResult.TryGetProperty("stringCode", out var sellSc) ? sellSc.GetString() : "UNKNOWN";
        if (sellCode == "TRADE_RETCODE_DONE")
            return $"SELL FILLED: {lots} lot(s) XAUUSD @ ~${price:F2} | Reason: {reason}";

        var sellMsg = sellResult.TryGetProperty("message", out var sellM) ? sellM.GetString() : sellResult.ToString();
        return $"SELL FAILED ({sellCode}): {sellMsg}";
    }

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
