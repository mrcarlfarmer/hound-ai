using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Trading.AlpacaClient;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace Hound.Trading.Nodes.Analysts;

/// <summary>
/// Technical market analyst — reads OHLCV bars from Alpaca and reports on
/// trend, momentum, support/resistance, and volume.
/// </summary>
public sealed class MarketAnalyst : AnalystBase
{
    public override string Name => "MarketAnalyst";

    private readonly IAlpacaService _alpacaService;

    public MarketAnalyst(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
        : base(activityLogger)
    {
        _alpacaService = alpacaService;

        // NOTE: tools are bound via method-group delegates rather than
        // attributed lambdas. AIFunctionFactory's reflection invoker has a bug
        // in 10.4.x that mangles `NullableContextAttribute` (loads it as
        // `lableContextAttribute`) when invoking synthesised lambda closures
        // with `[Description]` parameter attributes, which breaks the first
        // tool call in the analysts pipeline.
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                FetchStockDataAsync,
                "get_stock_data",
                "Retrieves OHLCV price bars for a symbol in a date range"),
        };

        Configure(new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a trading assistant tasked with analysing financial markets.
                Use the get_stock_data tool to retrieve recent OHLCV price bars.

                LANGUAGE: Write your entire report in ENGLISH. Do not use Chinese,
                Mandarin, or any other non-English characters. Prices are in US
                dollars (USD, $) — never yuan/元, euro, or any other currency.

                CRITICAL DATA-TRUST RULES:
                - The get_stock_data tool returns AUTHORITATIVE live market data from
                  the broker API. Trust it completely.
                - Your training cutoff is irrelevant. If a date looks "future" to you
                  but the tool returned rows for it, the data is real. Do NOT claim
                  the data is unavailable, simulated, or in the future.
                - The only time data is unavailable is when the tool explicitly
                  returns a message starting with "NO_DATA" or "ERROR". In every
                  other case, the bars are real and must be analysed as-is.

                PRICE-LEVEL RULES — STRICTLY ENFORCED:
                - The user prompt supplies "Current price" — this is the authoritative
                  last close.
                - Any price level you mention (support, resistance, moving averages,
                  targets, stops) MUST be derived from the actual bars returned by
                  the tool and MUST be within ±15% of the current price.
                - Do NOT invent round-number levels (e.g. $80, $90, $100) that
                  contradict the current price. Do NOT cite levels for a different
                  ticker.

                Analyse the data for trend direction, momentum, and volume patterns.
                Select and discuss the most relevant technical observations (moving averages,
                support/resistance, RSI-like momentum, volume changes, volatility).
                Write a detailed report with specific, actionable insights.
                End with a Markdown table summarising key observations.
                """,
            name: Name,
            description: "Technical market analyst — price action and indicators",
            tools: tools,
            loggerFactory: loggerFactory));
    }

    private async Task<string> FetchStockDataAsync(
        [Description("Ticker symbol, e.g. AAPL")] string symbol,
        [Description("Start date yyyy-MM-dd")] string startDate,
        [Description("End date yyyy-MM-dd")] string endDate)
    {
        try
        {
            var from = DateTime.TryParse(startDate, out var f) ? f : DateTime.UtcNow.Date.AddDays(-7);
            var to = DateTime.TryParse(endDate, out var t) ? t : DateTime.UtcNow.Date;
            var bars = await _alpacaService.GetBarsAsync(symbol, from, to, BarTimeFrame.Day);

            if (bars.Count == 0)
                return $"NO_DATA: broker returned zero bars for {symbol} between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";

            var summary = bars.Select(b =>
                $"{b.TimeUtc:yyyy-MM-dd}: O={b.Open} H={b.High} L={b.Low} C={b.Close} V={b.Volume}");

            // Prefix with an authoritative banner so the LLM cannot dismiss the
            // data as "future" or "simulated" based on its training cutoff.
            return $"""
                AUTHORITATIVE LIVE BROKER DATA for {symbol} ({from:yyyy-MM-dd} to {to:yyyy-MM-dd}).
                These bars are real and current. Analyse them as-is; do not question their validity.

                {string.Join("\n", summary)}
                """;
        }
        catch (Exception ex)
        {
            return $"ERROR fetching stock data for {symbol}: {ex.Message}";
        }
    }
}
