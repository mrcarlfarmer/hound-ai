using Hound.Core.Logging;
using Hound.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent: Determines trading strategy based on market context from AnalysisHound.
/// Outputs strategy decisions (buy/sell/hold, asset, reasoning).
/// </summary>
public class StrategyHound
{
    private const string HoundId = "strategy-hound";
    private const string PackId = "trading-pack";

    private readonly ChatClientAgent _agent;
    private readonly IActivityLogger _activityLogger;

    public StrategyHound(
        IChatClient chatClient,
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are StrategyHound, an algorithmic trading strategist.
                Given a market analysis (JSON), decide whether to buy, sell, or hold.
                Consider the trend, confidence score, and volume change.
                - Confidence >= 0.7 and Bullish trend => Buy
                - Confidence >= 0.7 and Bearish trend => Sell
                - Otherwise => Hold
                Respond strictly in JSON matching:
                {"symbol":"...","action":"Buy|Sell|Hold","quantity":0.0,"reasoning":"...","confidence":0.0}
                """,
            name: "StrategyHound",
            description: "Determines buy/sell/hold strategy based on market analysis",
            loggerFactory: loggerFactory);
    }

    public async Task<TradingDecision> DecideAsync(
        MarketAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = HoundId,
            HoundName = "StrategyHound",
            Message = $"Determining strategy for {analysis.Symbol}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var analysisJson = JsonSerializer.Serialize(analysis);

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"Market analysis:\n{analysisJson}\n\nWhat is your trading decision?")],
            session,
            cancellationToken: cancellationToken);

        var json = response.Text ?? "{}";

        try
        {
            var decision = JsonSerializer.Deserialize<TradingDecision>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            decision ??= new TradingDecision(analysis.Symbol, TradeAction.Hold, 0, "No decision", 0);

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = HoundId,
                HoundName = "StrategyHound",
                Message = $"Decision for {analysis.Symbol}: {decision.Action} (confidence {decision.Confidence:P0})",
                Severity = ActivitySeverity.Success,
            }, cancellationToken);

            return decision;
        }
        catch (JsonException)
        {
            return new TradingDecision(analysis.Symbol, TradeAction.Hold, 0, json, 0);
        }
    }
}
