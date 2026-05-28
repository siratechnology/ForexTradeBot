using ForexBot.Models;

namespace ForexBot.Services;

public class TradingEngine : ITradingEngine
{
    private readonly Portfolio _portfolio = new();
    private decimal _lastKnownPrice;

    private const decimal OzPerLot = 100m;

    public Task<string> BuyAsync(decimal price, decimal lots, string reason)
    {
        lots = Math.Max(0.01m, Math.Round(lots, 2));
        var oz   = lots * OzPerLot;
        var cost = price * oz;

        if (cost > _portfolio.CashBalance)
        {
            var affordableOz = Math.Floor(_portfolio.CashBalance / price * 100m) / 100m;
            lots = Math.Floor(affordableOz / OzPerLot * 100m) / 100m;
            if (lots < 0.01m)
                return Task.FromResult("REJECTED: Insufficient funds. Need at least 0.01 lot.");
            oz   = lots * OzPerLot;
            cost = price * oz;
        }

        _portfolio.CashBalance   -= cost;
        _portfolio.GoldHolding   += oz;
        _portfolio.GoldEntryValue += cost;
        _portfolio.Trades.Add(new Trade(Guid.NewGuid(), TradeType.Buy, price, oz, DateTime.UtcNow, reason));

        return Task.FromResult(
            $"BUY {lots} lot(s) ({oz:F0} oz) @ ${price:F2} = ${cost:F2} | Cash: ${_portfolio.CashBalance:F2} | Gold: {_portfolio.GoldHolding / OzPerLot:F2} lot(s)");
    }

    public Task<string> SellAsync(decimal price, decimal lots, string reason)
    {
        lots = Math.Max(0.01m, Math.Round(lots, 2));
        var oz = lots * OzPerLot;

        if (oz > _portfolio.GoldHolding)
            oz = _portfolio.GoldHolding;

        if (oz < 1m)
            return Task.FromResult("REJECTED: No gold holdings to sell.");

        var proceeds = price * oz;
        // Reduce cost basis proportionally
        if (_portfolio.GoldHolding > 0)
            _portfolio.GoldEntryValue -= _portfolio.GoldEntryValue * (oz / _portfolio.GoldHolding);
        _portfolio.GoldHolding -= oz;
        _portfolio.CashBalance += proceeds;
        _portfolio.Trades.Add(new Trade(Guid.NewGuid(), TradeType.Sell, price, oz, DateTime.UtcNow, reason));

        return Task.FromResult(
            $"SELL {oz / OzPerLot:F2} lot(s) ({oz:F0} oz) @ ${price:F2} = ${proceeds:F2} | Cash: ${_portfolio.CashBalance:F2} | Gold: {_portfolio.GoldHolding / OzPerLot:F2} lot(s)");
    }

    public Task<string> GetPortfolioStatusAsync(decimal currentPrice)
    {
        var totalValue = _portfolio.GetTotalValue(currentPrice);
        var pnl        = _portfolio.GetPnL(currentPrice);
        var pnlPct     = pnl / 10_000m * 100m;

        return Task.FromResult($"""
            === PAPER TRADING PORTFOLIO ===
            Cash Balance : ${_portfolio.CashBalance:F2}
            Gold Holding : {_portfolio.GoldHolding / OzPerLot:F2} lot(s) ({_portfolio.GoldHolding:F1} oz)
            Gold Value   : ${_portfolio.GoldHolding * currentPrice:F2}
            Total Value  : ${totalValue:F2}
            P&L          : ${pnl:F2} ({pnlPct:+0.00;-0.00}%)
            Total Trades : {_portfolio.Trades.Count}
            Current Price: ${currentPrice:F2}/oz
            ================================
            """);
    }

    public Task<(decimal floatPnL, bool hasPosition)> GetPositionStatusAsync(decimal currentPrice)
    {
        _lastKnownPrice = currentPrice;
        var hasPos  = _portfolio.GoldHolding > 0;
        var pnl     = hasPos ? _portfolio.GoldHolding * currentPrice - _portfolio.GoldEntryValue : 0m;
        return Task.FromResult((pnl, hasPos));
    }

    public Task<string> CloseAllAsync(string reason)
    {
        if (_portfolio.GoldHolding <= 0) return Task.FromResult("No position to close.");
        return SellAsync(_lastKnownPrice, _portfolio.GoldHolding / OzPerLot, reason);
    }

    public Portfolio Portfolio => _portfolio;
}
