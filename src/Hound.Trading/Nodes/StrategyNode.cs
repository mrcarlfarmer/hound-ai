using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.Graph;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hound.Trading.Nodes;

/// <summary>
/// Determines trading strategy based on market context from DataNode.
/// On refinement loops, incorporates RiskNode rejection reasoning as additional context.
/// Uses <c>qwen3:14b</c> via the <c>"strategy"</c> keyed IChatClient.
/// </summary>
public class StrategyNode : INode
{
    public string NodeId => "strategy-node";
    public string PackId => "trading-pack";

    private readonly ChatClientAgent _agent;
    private readonly IActivityLogger _activityLogger;

    public StrategyNode(
        IChatClient chatClient,
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are StrategyNode, an algorithmic trading strategist.
                Given a market analysis (JSON), decide whether to buy, sell, or hold.
                Consider the trend, confidence score, and volume change.
                - Confidence >= 0.7 and Bullish trend => Buy
                - Confidence >= 0.7 and Bearish trend => Sell
                - Otherwise => Hold
                If you receive risk rejection feedback, address the specific concerns raised
                and adjust your strategy accordingly.
                Respond strictly in JSON matching:
                {"symbol":"...","action":"Buy|Sell|Hold","quantity":0.0,"reasoning":"...","confidence":0.0}
                """,
            name: "StrategyNode",
            description: "Determines buy/sell/hold strategy based on market analysis",
            loggerFactory: loggerFactory);
    }

    public async Task<TradingGraphState> ExecuteAsync(
        TradingGraphState state, CancellationToken cancellationToken)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "StrategyNode",
            Message = $"Determining strategy for {state.Symbol}" +
                      (state.RefinementCount > 0 ? $" (refinement #{state.RefinementCount})" : string.Empty),
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var analysisJson = JsonSerializer.Serialize(state.DataOutput);

        var prompt = $"Market analysis:\n{analysisJson}\n\nWhat is your trading decision?";

        // On refinement loops, inject the risk rejection reasoning
        if (state.RefinementCount > 0 && state.RiskOutput is not null)
        {
            prompt += $"\n\nPrevious risk rejection (attempt #{state.RefinementCount}):\n{state.RiskOutput.Reasoning}" +
                      "\n\nPlease address these risk concerns and adjust your strategy.";
        }

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            session,
            cancellationToken: cancellationToken);

        var json = LlmResponseParser.ExtractJson(response.Text ?? "{}");
        TradingDecision decision;

        try
        {
            var result = JsonSerializer.Deserialize<TradingDecision>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
            decision = result ?? new TradingDecision(state.Symbol, TradeAction.Hold, 0, "No decision", 0);
        }
        catch (JsonException)
        {
            decision = new TradingDecision(state.Symbol, TradeAction.Hold, 0, json, 0);
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "StrategyNode",
            Message = $"Decision for {state.Symbol}: {decision.Action} (confidence {decision.Confidence:P0})",
            Severity = ActivitySeverity.Success,
        }, cancellationToken);

        return state with { StrategyOutput = decision };
    }
}
