using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using ForexBot.Models;

namespace ForexBot.Services;

public class GoldTradingBot
{
    private readonly AnthropicClient _client;
    private readonly IMarketDataService _market;
    private readonly ITradingEngine _engine;

    private const string SystemPrompt = """
        You are an expert XAUUSD (Gold/USD) trader. You analyze market data and make precise trading decisions.

        Your decision process:
        1. Call get_technical_indicators to assess current market conditions
        2. Call get_market_data with different periods (1h, 4h, 1d) to understand price action across timeframes
        3. Call get_portfolio to check balance, equity, and open positions
        4. Based on your analysis, either execute_buy, execute_sell, or hold (make no trade)
        5. Always provide a clear, concise reason for your decision

        Trading rules:
        - Volume is in LOTS. 1 lot = 100 oz XAUUSD (~$336,000 at $3,360/oz). Minimum trade: 0.01 lot.
        - Never risk more than 5% of balance in a single trade (use 0.01–0.05 lots for a $10,000 account)
        - RSI: oversold <30 = bullish signal | overbought >70 = bearish signal
        - Bollinger Bands: price near lower band = potential buy | near upper band = potential sell
        - Confirm signals with MA20/MA50 trend direction
        - ATR shows volatility: higher ATR = smaller position size
        - If signals are mixed or unclear, hold and wait for next cycle
        """;

    public GoldTradingBot(AnthropicClient client, IMarketDataService market, ITradingEngine engine)
    {
        _client = client;
        _market = market;
        _engine = engine;
    }

    public async Task AnalyzeAndTradeAsync(int cycleNumber)
    {
        await _market.TickAsync();
        Console.WriteLine($"\n--- Cycle {cycleNumber} | XAUUSD: ${_market.CurrentPrice:F2} | {DateTime.UtcNow:HH:mm:ss} UTC ---");

        var prompt = $"Cycle {cycleNumber}: XAUUSD is currently at ${_market.CurrentPrice:F2}. Analyze the market and make your trading decision.";
        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = prompt }
        };

        while (true)
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model        = "claude-opus-4-7",
                MaxTokens    = 8192,
                System       = SystemPrompt,
                Thinking     = new ThinkingConfigAdaptive { Display = Display.Summarized },
                OutputConfig = new OutputConfig { Effort = Effort.High },
                Tools        = BuildTools(),
                Messages     = messages,
            });

            var assistantContent = new List<ContentBlockParam>();
            var toolResults      = new List<(string id, Task<string> resultTask)>();

            foreach (var block in response.Content)
            {
                if (block.TryPickThinking(out ThinkingBlock? thinking))
                {
                    var preview = thinking.Thinking ?? "";
                    if (preview.Length > 200) preview = preview[..200] + "...";
                    Console.WriteLine($"  [thinking] {preview}");
                    assistantContent.Add(new ThinkingBlockParam
                    {
                        Thinking  = thinking.Thinking ?? string.Empty,
                        Signature = thinking.Signature ?? string.Empty,
                    });
                }
                else if (block.TryPickText(out TextBlock? text))
                {
                    Console.WriteLine($"  [claude] {text.Text}");
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                {
                    Console.WriteLine($"  [tool] {toolUse.Name}");
                    assistantContent.Add(new ToolUseBlockParam
                    {
                        ID    = toolUse.ID,
                        Name  = toolUse.Name,
                        Input = toolUse.Input,
                    });
                    toolResults.Add((toolUse.ID, DispatchToolAsync(toolUse.Name, toolUse.Input)));
                }
            }

            if (response.StopReason != "tool_use") break;

            // Await all tool results
            var toolResultBlocks = new List<ContentBlockParam>();
            foreach (var (id, task) in toolResults)
            {
                var result = await task;
                Console.WriteLine($"  [result] {result[..Math.Min(120, result.Length)]}");
                toolResultBlocks.Add(new ToolResultBlockParam { ToolUseID = id, Content = result });
            }

            messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });
            messages.Add(new MessageParam { Role = Role.User,      Content = toolResultBlocks });
        }
    }

    private async Task<string> DispatchToolAsync(string name, IReadOnlyDictionary<string, JsonElement> input)
    {
        return name switch
        {
            "get_technical_indicators" => GetTechnicalIndicators(),
            "get_market_data"          => GetMarketData(input),
            "get_portfolio"            => await _engine.GetPortfolioStatusAsync(_market.CurrentPrice),
            "execute_buy"              => await ExecuteBuyAsync(input),
            "execute_sell"             => await ExecuteSellAsync(input),
            _                          => $"Unknown tool: {name}",
        };
    }

    private string GetTechnicalIndicators()
    {
        var i = _market.CalculateIndicators();
        return $"""
            RSI(14)         : {i.RSI:F2}
            MA20            : ${i.MA20:F2}
            MA50            : ${i.MA50:F2}
            Bollinger Upper : ${i.BollingerUpper:F2}
            Bollinger Lower : ${i.BollingerLower:F2}
            ATR(14)         : ${i.ATR:F2}
            Trend           : {i.Trend}
            Current Price   : ${_market.CurrentPrice:F2}
            """;
    }

    private string GetMarketData(IReadOnlyDictionary<string, JsonElement> input)
    {
        var period  = input.TryGetValue("period", out var p) ? p.GetString() ?? "1h" : "1h";
        var count   = input.TryGetValue("count",  out var c) ? c.GetInt32() : 20;
        count = Math.Clamp(count, 5, 50);

        var candles = _market.GetHistory(period, count);
        var sb = new StringBuilder();
        sb.AppendLine($"XAUUSD {period} — last {candles.Count} candles:");
        sb.AppendLine($"  {"Timestamp",-20} {"Open",8} {"High",8} {"Low",8} {"Close",8}");
        foreach (var c2 in candles.TakeLast(10))
            sb.AppendLine($"  {c2.Timestamp:yyyy-MM-dd HH:mm,-20} {c2.Open,8:F2} {c2.High,8:F2} {c2.Low,8:F2} {c2.Close,8:F2}");
        return sb.ToString();
    }

    private async Task<string> ExecuteBuyAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var lots   = input.TryGetValue("volume", out var q) ? (decimal)q.GetDouble() : 0.01m;
        var reason = input.TryGetValue("reason", out var r) ? r.GetString() ?? "AI signal" : "AI signal";
        return await _engine.BuyAsync(_market.CurrentPrice, lots, reason);
    }

    private async Task<string> ExecuteSellAsync(IReadOnlyDictionary<string, JsonElement> input)
    {
        var lots   = input.TryGetValue("volume", out var q) ? (decimal)q.GetDouble() : 0.01m;
        var reason = input.TryGetValue("reason", out var r) ? r.GetString() ?? "AI signal" : "AI signal";
        return await _engine.SellAsync(_market.CurrentPrice, lots, reason);
    }

    private static IReadOnlyList<ToolUnion> BuildTools() =>
    [
        new Tool
        {
            Name        = "get_technical_indicators",
            Description = "Get current RSI(14), MA20, MA50, Bollinger Bands, ATR(14), and trend for XAUUSD.",
            InputSchema = new InputSchema { Properties = new Dictionary<string, JsonElement>() },
        },
        new Tool
        {
            Name        = "get_market_data",
            Description = "Get OHLCV candlestick history for XAUUSD. Call with multiple periods for multi-timeframe analysis.",
            InputSchema = new InputSchema
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["period"] = JsonSerializer.SerializeToElement(new
                    {
                        type        = "string",
                        description = "Timeframe: 1h (hourly), 4h (4-hour), 1d (daily)",
                        @enum       = new[] { "1h", "4h", "1d" },
                    }),
                    ["count"] = JsonSerializer.SerializeToElement(new
                    {
                        type        = "integer",
                        description = "Number of candles (5–50)",
                        minimum     = 5,
                        maximum     = 50,
                    }),
                },
                Required = ["period"],
            },
        },
        new Tool
        {
            Name        = "get_portfolio",
            Description = "Get current account balance, equity, open positions, and P&L.",
            InputSchema = new InputSchema { Properties = new Dictionary<string, JsonElement>() },
        },
        new Tool
        {
            Name        = "execute_buy",
            Description = "Open a BUY position on XAUUSD at the current market price.",
            InputSchema = new InputSchema
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["volume"] = JsonSerializer.SerializeToElement(new
                    {
                        type        = "number",
                        description = "Volume in lots. 1 lot = 100 oz XAUUSD. Minimum 0.01 lots.",
                        minimum     = 0.01,
                    }),
                    ["reason"] = JsonSerializer.SerializeToElement(new
                    {
                        type        = "string",
                        description = "Brief reason for this buy decision",
                    }),
                },
                Required = ["volume", "reason"],
            },
        },
        new Tool
        {
            Name        = "execute_sell",
            Description = "Close open BUY positions or open a SELL position on XAUUSD.",
            InputSchema = new InputSchema
            {
                Properties = new Dictionary<string, JsonElement>
                {
                    ["volume"] = JsonSerializer.SerializeToElement(new
                    {
                        type        = "number",
                        description = "Volume in lots to sell. Minimum 0.01 lots.",
                        minimum     = 0.01,
                    }),
                    ["reason"] = JsonSerializer.SerializeToElement(new
                    {
                        type        = "string",
                        description = "Brief reason for this sell decision",
                    }),
                },
                Required = ["volume", "reason"],
            },
        },
    ];
}
