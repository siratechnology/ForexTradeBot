namespace ForexBot.Models;

public interface IMarketDataService
{
    decimal CurrentPrice { get; }
    Task TickAsync();
    List<Candle> GetHistory(string period, int count);
    TechnicalIndicators CalculateIndicators();
}

public interface ITradingEngine
{
    // Opens an independent position. A buy and a sell can coexist — neither nets the other.
    Task<string> OpenAsync(TradeType side, decimal price, decimal lots, string reason);
    // Closes a single position by its id (locks in its own profit/loss).
    Task<string> ClosePositionAsync(string positionId, string reason);

    Task<string> BuyAsync(decimal price, decimal lots, string reason);
    Task<string> SellAsync(decimal price, decimal lots, string reason);
    Task<string> GetPortfolioStatusAsync(decimal currentPrice);
    Task<(decimal floatPnL, bool hasPosition)> GetPositionStatusAsync(decimal currentPrice);
    Task<string> CloseAllAsync(string reason);
    Task<List<OpenPosition>> GetOpenPositionsAsync();
}
