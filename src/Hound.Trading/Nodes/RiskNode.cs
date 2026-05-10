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
/// Risk management node. Evaluates proposed trades against portfolio exposure,
/// position limits, and max drawdown rules. Approves, rejects, or modifies orders.
/// <para>
/// When the verdict is <see cref="RiskVerdict.Rejected"/> and the refinement count
/// is below the configured maximum, the graph loops back to <see cref="StrategyNode"/>
/// with the rejection reasoning as additional context.
/// </para>
/// </summary>
public class RiskNode : INode
{
    public string NodeId => "risk-node";
    public string PackId => "trading-pack";

    private readonly ChatClientAgent _agent;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;

    public RiskNode(
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
                () => GetPortfolioSummaryAsync(),
                "get_portfolio",
                "Retrieves current account equity and open positions"),
        };

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are RiskNode, a risk management specialist.
                Evaluate the proposed trade against these rules:
                - Maximum single-position size: 20% of portfolio equity
                - Maximum total exposure: 80% of equity in equities
                - Never exceed quantity of 1000 shares per order
                Use the get_portfolio tool to check current state.
                Respond strictly in JSON matching:
                {"verdict":"Approved|Rejected|Modified","decision":{...original decision...},"reasoning":"...","adjustedQuantity":null}
                Set adjustedQuantity only when verdict is Modified.
                """,
            name: "RiskNode",
            description: "Evaluates proposed trades against risk limits and portfolio exposure",
            tools: tools,
            loggerFactory: loggerFactory);
    }

    public async Task<TradingGraphState> ExecuteAsync(
        TradingGraphState state, CancellationToken cancellationToken)
    {
        var decision = state.StrategyOutput!;

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "RiskNode",
            Message = $"Evaluating risk for {decision.Action} {decision.Quantity} {decision.Symbol}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var decisionJson = JsonSerializer.Serialize(decision);

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"Evaluate this proposed trade:\n{decisionJson}")],
            session,
            cancellationToken: cancellationToken);

        var json = LlmResponseParser.ExtractJson(response.Text ?? "{}");
        RiskAssessment assessment;

        try
        {
            var result = JsonSerializer.Deserialize<RiskAssessment>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
            assessment = result ?? new RiskAssessment(RiskVerdict.Rejected, decision, "Unable to parse risk assessment");
        }
        catch (JsonException)
        {
            assessment = new RiskAssessment(RiskVerdict.Rejected, decision, json);
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "RiskNode",
            Message = $"Risk verdict for {decision.Symbol}: {assessment.Verdict} — {assessment.Reasoning}",
            Severity = assessment.Verdict == RiskVerdict.Rejected ? ActivitySeverity.Warning : ActivitySeverity.Success,
        }, cancellationToken);

        // When rejected, increment refinement count and clear strategy/risk outputs
        // so the graph can loop back to StrategyNode with fresh state
        if (assessment.Verdict == RiskVerdict.Rejected)
        {
            return state with
            {
                RiskOutput = assessment,
                StrategyOutput = null,
                RefinementCount = state.RefinementCount + 1,
            };
        }

        return state with { RiskOutput = assessment };
    }

    private async Task<string> GetPortfolioSummaryAsync()
    {
        var account = await _alpacaService.GetAccountAsync();
        var positions = await _alpacaService.ListPositionsAsync();

        var positionSummary = positions.Select(p =>
            $"{p.Symbol}: {p.Quantity} shares, market value ${p.MarketValue:F2}");

        return $"Equity: ${account.Equity:F2}\n" +
               $"Cash: ${account.TradableCash:F2}\n" +
               $"Positions:\n{string.Join("\n", positionSummary)}";
    }
}
