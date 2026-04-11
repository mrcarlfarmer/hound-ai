using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent: Risk management. Evaluates proposed trades against portfolio exposure,
/// position limits, max drawdown rules. Approves/rejects/modifies orders.
/// </summary>
public class RiskHound
{
    private const string HoundId = "risk-hound";
    private const string PackId = "trading-pack";

    private readonly ChatClientAgent _agent;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;

    public RiskHound(
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
                You are RiskHound, a risk management specialist.
                Evaluate the proposed trade against these rules:
                - Maximum single-position size: 20% of portfolio equity
                - Maximum total exposure: 80% of equity in equities
                - Never exceed quantity of 1000 shares per order
                Use the get_portfolio tool to check current state.
                Respond strictly in JSON matching:
                {"verdict":"Approved|Rejected|Modified","decision":{...original decision...},"reasoning":"...","adjustedQuantity":null}
                Set adjustedQuantity only when verdict is Modified.
                """,
            name: "RiskHound",
            description: "Evaluates proposed trades against risk limits and portfolio exposure",
            tools: tools,
            loggerFactory: loggerFactory);
    }

    public async Task<RiskAssessment> EvaluateAsync(
        TradingDecision decision,
        CancellationToken cancellationToken = default)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = HoundId,
            HoundName = "RiskHound",
            Message = $"Evaluating risk for {decision.Action} {decision.Quantity} {decision.Symbol}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var decisionJson = JsonSerializer.Serialize(decision);

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"Evaluate this proposed trade:\n{decisionJson}")],
            session,
            cancellationToken: cancellationToken);

        var json = response.Text ?? "{}";

        try
        {
            var assessment = JsonSerializer.Deserialize<RiskAssessment>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            assessment ??= new RiskAssessment(RiskVerdict.Rejected, decision, "Unable to parse risk assessment");

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = HoundId,
                HoundName = "RiskHound",
                Message = $"Risk verdict for {decision.Symbol}: {assessment.Verdict} — {assessment.Reasoning}",
                Severity = assessment.Verdict == RiskVerdict.Rejected ? ActivitySeverity.Warning : ActivitySeverity.Success,
            }, cancellationToken);

            return assessment;
        }
        catch (JsonException)
        {
            return new RiskAssessment(RiskVerdict.Rejected, decision, json);
        }
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
