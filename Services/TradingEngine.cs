using ForexBot.Models;

namespace ForexBot.Services;

// Paper-trading engine that models independent long/short positions, like an MT5 hedging account.
// A buy and a sell can be open at the same time — neither nets the other. This mirrors
// MetaApiTradingEngine so the strategy behaves the same in paper and live.
public class TradingEngine : ITradingEngine
{
    private sealed class PaperPosition
    {
        public string    Id        { get; } = Guid.NewGuid().ToString("N")[..12];
        public TradeType Side      { get; init; }
        public decimal   Lots      { get; init; }
        public decimal   OpenPrice { get; init; }
        public DateTime  OpenedAt  { get; } = DateTime.UtcNow;
    }

    private const decimal OzPerLot       = 100m;
    private const decimal Leverage       = 100m;     // typical 1:100 margin
    private const decimal InitialBalance = 10_000m;

    private readonly List<PaperPosition> _positions = new();
    private decimal _balance = InitialBalance;       // realized equity (grows/shrinks as positions close)
    private decimal _lastKnownPrice;

    private static decimal PositionPnL(PaperPosition p, decimal price) =>
        (p.Side == TradeType.Buy ? price - p.OpenPrice : p.OpenPrice - price) * p.Lots * OzPerLot;

    private decimal FloatingPnL(decimal price) => _positions.Sum(p => PositionPnL(p, price));
    private decimal UsedMargin => _positions.Sum(p => p.Lots * OzPerLot * p.OpenPrice / Leverage);

    public Task<string> OpenAsync(TradeType side, decimal price, decimal lots, string reason)
    {
        _lastKnownPrice = price;
        lots = Math.Max(0.01m, Math.Round(lots, 2));

        var requiredMargin = lots * OzPerLot * price / Leverage;
        var equity         = _balance + FloatingPnL(price);
        var freeMargin     = equity - UsedMargin;
        if (requiredMargin > freeMargin)
            return Task.FromResult($"REJECTED: not enough free margin (need ${requiredMargin:F2}, free ${freeMargin:F2}).");

        var pos = new PaperPosition { Side = side, Lots = lots, OpenPrice = price };
        _positions.Add(pos);
        return Task.FromResult(
            $"OPEN {SideLabel(side)} {lots} lot @ ${price:F2} [{pos.Id}] | open: {_positions.Count}");
    }

    public Task<string> ClosePositionAsync(string positionId, string reason)
    {
        var pos = _positions.FirstOrDefault(p => p.Id == positionId);
        if (pos == null) return Task.FromResult($"Position {positionId} not found.");

        var pnl = PositionPnL(pos, _lastKnownPrice);
        _balance += pnl;
        _positions.Remove(pos);
        return Task.FromResult(
            $"CLOSE {SideLabel(pos.Side)} {pos.Lots} lot @ ${_lastKnownPrice:F2} [{pos.Id}] | P&L ${pnl:+0.00;-0.00} | {reason}");
    }

    // Manual API trades open independent positions (no netting), same as the bot.
    public Task<string> BuyAsync(decimal price, decimal lots, string reason)  => OpenAsync(TradeType.Buy,  price, lots, reason);
    public Task<string> SellAsync(decimal price, decimal lots, string reason) => OpenAsync(TradeType.Sell, price, lots, reason);

    public Task<(decimal floatPnL, bool hasPosition)> GetPositionStatusAsync(decimal currentPrice)
    {
        _lastKnownPrice = currentPrice;
        return Task.FromResult((FloatingPnL(currentPrice), _positions.Count > 0));
    }

    public Task<string> CloseAllAsync(string reason)
    {
        if (_positions.Count == 0) return Task.FromResult("No positions to close.");
        var count = _positions.Count;
        var total = FloatingPnL(_lastKnownPrice);
        _balance += total;
        _positions.Clear();
        return Task.FromResult($"CLOSED {count} position(s) | total P&L ${total:+0.00;-0.00} | {reason}");
    }

    public Task<List<OpenPosition>> GetOpenPositionsAsync()
    {
        var list = _positions
            .Select(p => new OpenPosition(p.Id, SideLabel(p.Side), p.Lots, p.OpenPrice, PositionPnL(p, _lastKnownPrice)))
            .ToList();
        return Task.FromResult(list);
    }

    public Task<string> GetPortfolioStatusAsync(decimal currentPrice)
    {
        _lastKnownPrice = currentPrice;
        var floating = FloatingPnL(currentPrice);
        var equity   = _balance + floating;
        return Task.FromResult($"""
            === PAPER TRADING PORTFOLIO ===
            Balance      : ${_balance:F2}
            Equity       : ${equity:F2}
            Open Float   : ${floating:+0.00;-0.00}
            Open Positions: {_positions.Count}
            Current Price: ${currentPrice:F2}/oz
            ================================
            """);
    }

    private static string SideLabel(TradeType side) => side == TradeType.Buy ? "BUY" : "SELL";
}
