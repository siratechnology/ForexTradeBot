namespace ForexBot.Models;

public class Portfolio
{
    private const decimal InitialCapital = 10_000m;

    public decimal CashBalance  { get; set; } = InitialCapital;
    public decimal GoldHolding  { get; set; } = 0m;
    public decimal GoldEntryValue { get; set; } = 0m; // cost basis for unrealized P&L
    public List<Trade> Trades { get; } = [];

    public decimal GetTotalValue(decimal currentPrice) => CashBalance + GoldHolding * currentPrice;
    public decimal GetPnL(decimal currentPrice) => GetTotalValue(currentPrice) - InitialCapital;
}
