using Hound.Core.Logging;
using Hound.Trading.AlpacaClient;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace Hound.Trading.Nodes.Analysts;

/// <summary>
/// Fundamentals analyst — pulls account equity, buying power, and current
/// position context from Alpaca and reports on the financial backdrop.
/// </summary>
public sealed class FundamentalsAnalyst : AnalystBase
{
    public override string Name => "FundamentalsAnalyst";

    private readonly IAlpacaService _alpacaService;

    public FundamentalsAnalyst(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
        : base(activityLogger)
    {
        _alpacaService = alpacaService;

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                FetchFundamentalsAsync,
                "get_fundamentals",
                "Retrieves company fundamentals — profile, financials, account equity"),
        };

        Configure(new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a fundamentals analyst tasked with analysing company financial data.
                Use the get_fundamentals tool to retrieve available data.
                Analyse the account equity, buying power, and any position data.
                Write a comprehensive report on the financial context relevant for trading.
                Include assessment of capital availability and portfolio exposure.

                LANGUAGE: Write your entire report in ENGLISH. Do not use Chinese,
                Mandarin, or any other non-English characters. Prices are in US
                dollars (USD, $) — never yuan/元, euro, or any other currency.

                End with a Markdown table summarising key financial metrics.
                """,
            name: Name,
            description: "Fundamentals analyst — financial data and company profile",
            tools: tools,
            loggerFactory: loggerFactory));
    }

    private async Task<string> FetchFundamentalsAsync(
        [Description("Ticker symbol")] string symbol)
    {
        try
        {
            var account = await _alpacaService.GetAccountAsync();
            var positions = await _alpacaService.ListPositionsAsync();

            var positionSummary = positions
                .Where(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Select(p => $"  {p.Symbol}: {p.Quantity} shares, market value ${p.MarketValue:F2}, unrealized P&L ${p.UnrealizedProfitLoss:F2}");

            var otherPositions = positions
                .Where(p => !string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Select(p => $"  {p.Symbol}: {p.Quantity} shares, ${p.MarketValue:F2}");

            return $"""
                Account Fundamentals:
                Equity: ${account.Equity:F2}
                Cash: ${account.TradableCash:F2}
                Buying Power: ${account.BuyingPower:F2}

                Current {symbol} position:
                {(positionSummary.Any() ? string.Join("\n", positionSummary) : "  No existing position")}

                Other positions:
                {(otherPositions.Any() ? string.Join("\n", otherPositions) : "  None")}

                Total portfolio positions: {positions.Count}
                """;
        }
        catch (Exception ex)
        {
            return $"ERROR fetching fundamentals for {symbol}: {ex.Message}";
        }
    }
}
