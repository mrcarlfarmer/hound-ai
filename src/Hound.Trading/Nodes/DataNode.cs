using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hound.Trading.Nodes;

/// <summary>
/// Analyses market data (price bars, volume, indicators) for a symbol.
/// Produces <see cref="MarketAnalysis"/> with trend and confidence scores.
/// </summary>
public class DataNode : INode
{
    public string NodeId => "data-node";
    public string PackId => "trading-pack";

    private readonly ChatClientAgent _agent;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;

    public DataNode(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
    {
        _alpacaService = alpacaService;
        _activityLogger = activityLogger;

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Stock ticker symbol to analyse, e.g. AAPL")] string symbol) =>
                    FetchMarketDataAsync(symbol),
                "fetch_market_data",
                "Fetches recent price bars and volume data for a symbol"),
        };

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are DataNode, a quantitative market analyst.
                When asked to analyse a symbol, use the fetch_market_data tool to retrieve recent bars.
                Calculate the price trend (Bullish/Bearish/Neutral), percentage volume change vs the previous period,
                and assign a confidence score between 0 and 1.
                Respond strictly in JSON matching:
                {"symbol":"...","lastPrice":0.0,"volumeChange":0.0,"trend":"...","confidenceScore":0.0,"summary":"..."}
                """,
            name: "DataNode",
            description: "Analyses market data and produces recommendations with confidence scores",
            tools: tools,
            loggerFactory: loggerFactory);
    }

    public async Task<TradingGraphState> ExecuteAsync(
        TradingGraphState state, CancellationToken cancellationToken)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "DataNode",
            Message = $"Analysing symbol {state.Symbol}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"Analyse {state.Symbol} and provide your assessment.")],
            session,
            cancellationToken: cancellationToken);

        var json = LlmResponseParser.ExtractJson(response.Text ?? "{}");
        MarketAnalysis analysis;

        try
        {
            var result = JsonSerializer.Deserialize<MarketAnalysis>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
            analysis = result ?? new MarketAnalysis(state.Symbol, 0, 0, "Unknown", 0, "No analysis available");
        }
        catch (JsonException)
        {
            analysis = new MarketAnalysis(state.Symbol, 0, 0, "Unknown", 0, json);
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "DataNode",
            Message = $"Analysis complete for {state.Symbol}: {analysis.Trend} (confidence {analysis.ConfidenceScore:P0})",
            Severity = ActivitySeverity.Success,
        }, cancellationToken);

        return state with { DataOutput = analysis };
    }

    private async Task<string> FetchMarketDataAsync(string symbol)
    {
        try
        {
            var to = DateTime.UtcNow.Date;
            var from = to.AddDays(-7);
            var bars = await _alpacaService.GetBarsAsync(symbol, from, to, BarTimeFrame.Day);

            if (bars.Count == 0)
                return $"No bar data found for {symbol} in the last 7 days.";

            var summary = bars.Select(b =>
                $"{b.TimeUtc:yyyy-MM-dd}: O={b.Open} H={b.High} L={b.Low} C={b.Close} V={b.Volume}");

            return string.Join("\n", summary);
        }
        catch (Exception ex)
        {
            return $"ERROR fetching market data for {symbol}: {ex.Message}";
        }
    }

}
