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
                /no_think
                You are RiskNode, a risk management specialist.
                Evaluate the proposed trade using the PRE-COMPUTED RISK METRICS provided.
                Do NOT recalculate values — the metrics are authoritative.
                
                Rules:
                - Maximum single-position size: 20% of portfolio equity
                - Maximum total exposure: 80% of equity in equities
                - Never exceed quantity of 1000 shares per order
                
                Decision logic:
                - If position value exceeds 20% of equity → Reject or Modify (set adjustedQuantity to max shares within limit)
                - If total exposure exceeds 80% → Reject or Modify
                - If quantity exceeds 1000 → Modify with adjustedQuantity = 1000
                - Otherwise → Approve
                
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

        // Pre-compute risk metrics so the LLM doesn't need to do arithmetic
        var account = await _alpacaService.GetAccountAsync();
        var positions = await _alpacaService.ListPositionsAsync();

        var equity = account.Equity ?? 0m;
        var lastPrice = state.DataOutput?.LastPrice ?? 0m;
        var proposedValue = decision.Quantity * lastPrice;
        var positionPct = equity > 0 ? proposedValue / equity * 100 : 0;
        var existingExposure = positions.Sum(p => Math.Abs(p.MarketValue ?? 0m));
        var totalExposurePct = equity > 0 ? (existingExposure + proposedValue) / equity * 100 : 0;
        var maxPositionValue = equity * 0.20m;
        var maxQuantityByLimit = lastPrice > 0 ? Math.Floor(maxPositionValue / lastPrice) : 0;

        var riskContext = $"""
            PRE-COMPUTED RISK METRICS (use these, do NOT recalculate):
            - Account equity: ${equity:F2}
            - Share price ({decision.Symbol}): ${lastPrice:F2}
            - Proposed quantity: {decision.Quantity}
            - Proposed position value: {decision.Quantity} × ${lastPrice:F2} = ${proposedValue:F2}
            - Position as % of equity: {positionPct:F1}% (limit: 20%)
            - Max allowed position value: ${maxPositionValue:F2}
            - Max shares within 20% limit: {maxQuantityByLimit}
            - Existing equity exposure: ${existingExposure:F2}
            - Total exposure after trade: ${existingExposure + proposedValue:F2} ({totalExposurePct:F1}% of equity, limit: 80%)
            - Quantity cap per order: 1000 shares
            """;

        var decisionJson = JsonSerializer.Serialize(decision);

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"Evaluate this proposed trade:\n{decisionJson}\n\n{riskContext}")],
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
