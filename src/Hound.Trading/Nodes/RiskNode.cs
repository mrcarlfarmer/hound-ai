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
/// A <see cref="RiskVerdict.Rejected"/> verdict is only produced when the proposed
/// trade would exceed the 80% total-exposure hard cap. Since that limit applies to
/// the whole account, refining the order can't resolve it, and the graph terminates
/// the run rather than looping back to <see cref="StrategyNode"/>.
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
                Do NOT invent additional rules or constraints beyond those listed below.
                
                Rules (ONLY these three):
                1. Maximum single-position size: 20% of portfolio equity
                2. Maximum total exposure: 80% of equity in equities
                3. Maximum 1000 shares per order (fractional shares ARE allowed, e.g. 1.5287 is valid)
                
                Decision logic — use the pre-computed "Position as % of equity" and "Total exposure" values:
                - If position % > 20% → verdict: Modified, set adjustedQuantity to the "Max shares within 20% limit" value
                - If total exposure % > 80% → verdict: Rejected
                - If quantity > 1000 → verdict: Modified, adjustedQuantity = 1000
                - Otherwise → verdict: Approved (do NOT reject or modify for any other reason)
                
                Respond strictly in JSON matching:
                {"verdict":"Approved|Rejected|Modified","decision":{...copy the original decision object unchanged...},"reasoning":"one sentence explaining which rule passed or failed","adjustedQuantity":null}
                Set adjustedQuantity only when verdict is Modified. Keep reasoning brief.
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

        // The LLM sometimes returns `"reasoning":""` (or omits the field
        // entirely), which leaves the dashboard panel with a verdict and no
        // explanation. Fall back to a deterministic message derived from the
        // pre-computed metrics so reviewers always see *why* a run was
        // rejected / modified / approved.
        if (string.IsNullOrWhiteSpace(assessment.Reasoning))
        {
            var fallback = BuildFallbackReasoning(
                assessment.Verdict,
                positionPct,
                totalExposurePct,
                decision.Quantity,
                maxQuantityByLimit,
                assessment.AdjustedQuantity);
            assessment = assessment with { Reasoning = fallback };
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "RiskNode",
            Message = $"Risk verdict for {decision.Symbol}: {assessment.Verdict} — {assessment.Reasoning}",
            Severity = assessment.Verdict == RiskVerdict.Rejected ? ActivitySeverity.Warning : ActivitySeverity.Success,
        }, cancellationToken);

        // Rejected verdicts are driven by the 80% total-exposure hard cap,
        // which can't be resolved by refining the order. The graph router
        // terminates the run on Rejected, so we don't clear StrategyOutput
        // or bump the refinement counter here.
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

    /// <summary>
    /// Generates a deterministic reasoning string from the same pre-computed
    /// risk metrics the LLM saw. Used when the LLM returns no reasoning text
    /// so the dashboard never shows a verdict without an explanation.
    /// </summary>
    private static string BuildFallbackReasoning(
        RiskVerdict verdict,
        decimal positionPct,
        decimal totalExposurePct,
        decimal proposedQuantity,
        decimal maxQuantityByLimit,
        decimal? adjustedQuantity)
    {
        var reasonParts = new List<string>();

        if (totalExposurePct > 80m)
            reasonParts.Add($"projected total exposure {totalExposurePct:F1}% exceeds the 80% cap");
        if (positionPct > 20m)
            reasonParts.Add($"proposed position {positionPct:F1}% exceeds the 20% per-position limit (max {maxQuantityByLimit} shares)");
        if (proposedQuantity > 1000m)
            reasonParts.Add($"proposed quantity {proposedQuantity} exceeds the 1000-share per-order cap");

        return verdict switch
        {
            RiskVerdict.Rejected when reasonParts.Count > 0 =>
                $"Rejected: {string.Join("; ", reasonParts)}.",
            RiskVerdict.Rejected =>
                $"Rejected without explanation from the model. Pre-computed metrics: position {positionPct:F1}% of equity, total exposure {totalExposurePct:F1}%.",
            RiskVerdict.Modified when adjustedQuantity.HasValue =>
                $"Modified to {adjustedQuantity.Value} shares: {(reasonParts.Count > 0 ? string.Join("; ", reasonParts) : $"position {positionPct:F1}% / exposure {totalExposurePct:F1}%")}.",
            RiskVerdict.Modified =>
                $"Modified by the model: position {positionPct:F1}% of equity, total exposure {totalExposurePct:F1}%.",
            _ =>
                $"Approved: position {positionPct:F1}% of equity (limit 20%), total exposure {totalExposurePct:F1}% (limit 80%), quantity {proposedQuantity} (limit 1000).",
        };
    }
}
