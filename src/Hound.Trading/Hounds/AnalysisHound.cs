using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent: Analyses market data (price bars, volume, indicators).
/// Produces recommendations with confidence scores.
/// </summary>
public class AnalysisHound
{
    private const string HoundId = "analysis-hound";
    private const string PackId = "trading-pack";

    private readonly ChatClientAgent _agent;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;

    public AnalysisHound(
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
                You are AnalysisHound, a quantitative market analyst.
                When asked to analyse a symbol, use the fetch_market_data tool to retrieve recent bars.
                Calculate the price trend (Bullish/Bearish/Neutral), percentage volume change vs the previous period,
                and assign a confidence score between 0 and 1.
                Respond strictly in JSON matching:
                {"symbol":"...","lastPrice":0.0,"volumeChange":0.0,"trend":"...","confidenceScore":0.0,"summary":"..."}
                """,
            name: "AnalysisHound",
            description: "Analyses market data and produces recommendations with confidence scores",
            tools: tools,
            loggerFactory: loggerFactory);
    }

    public async Task<MarketAnalysis> AnalyseAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = HoundId,
            HoundName = "AnalysisHound",
            Message = $"Analysing symbol {symbol}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"Analyse {symbol} and provide your assessment.")],
            session,
            cancellationToken: cancellationToken);

        var json = response.Text ?? "{}";

        try
        {
            var result = JsonSerializer.Deserialize<MarketAnalysis>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var analysis = result ?? new MarketAnalysis(symbol, 0, 0, "Unknown", 0, "No analysis available");

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = HoundId,
                HoundName = "AnalysisHound",
                Message = $"Analysis complete for {symbol}: {analysis.Trend} (confidence {analysis.ConfidenceScore:P0})",
                Severity = ActivitySeverity.Success,
            }, cancellationToken);

            return analysis;
        }
        catch (JsonException)
        {
            return new MarketAnalysis(symbol, 0, 0, "Unknown", 0, json);
        }
    }

    private async Task<string> FetchMarketDataAsync(string symbol)
    {
        var to = DateTime.UtcNow.Date;
        var from = to.AddDays(-7);
        var bars = await _alpacaService.GetBarsAsync(symbol, from, to, BarTimeFrame.Day);

        if (bars.Count == 0)
            return $"No bar data found for {symbol}";

        var latest = bars[^1];
        var summary = bars.Select(b =>
            $"{b.TimeUtc:yyyy-MM-dd}: O={b.Open} H={b.High} L={b.Low} C={b.Close} V={b.Volume}");

        return string.Join("\n", summary);
    }
}
