namespace ForexBot.Models;

public record OpenPosition(
    string Id,
    string Type,
    decimal Lots,
    decimal OpenPrice,
    decimal PnL
);

public record TradeRecord(
    DateTime Time,
    string Action,
    decimal Lots,
    decimal Price,
    string Reason
);
