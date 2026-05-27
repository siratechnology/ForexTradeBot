namespace ForexBot.Models;

public record Candle(DateTime Timestamp, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);

public record TechnicalIndicators(
    decimal RSI,
    decimal MA20,
    decimal MA50,
    decimal BollingerUpper,
    decimal BollingerLower,
    decimal ATR,
    string Trend
);
