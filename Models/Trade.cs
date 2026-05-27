namespace ForexBot.Models;

public enum TradeType { Buy, Sell }

public record Trade(
    Guid Id,
    TradeType Type,
    decimal Price,
    decimal Quantity,
    DateTime Timestamp,
    string Reason
);
