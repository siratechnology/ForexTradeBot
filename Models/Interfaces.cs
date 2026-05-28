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
    Task<string> BuyAsync(decimal price, decimal lots, string reason);
    Task<string> SellAsync(decimal price, decimal lots, string reason);
    Task<string> GetPortfolioStatusAsync(decimal currentPrice);
    Task<(decimal floatPnL, bool hasPosition)> GetPositionStatusAsync(decimal currentPrice);
    Task<string> CloseAllAsync(string reason);
    Task<List<OpenPosition>> GetOpenPositionsAsync();
}
