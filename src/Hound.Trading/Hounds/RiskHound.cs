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
/// AF Agent: Risk management. Evaluates proposed trades against portfolio exposure,
/// position limits, max drawdown rules. Approves/rejects/modifies orders.
/// </summary>
public class RiskHound
{
    private const string HoundId = "risk-hound";
    private const string PackId = "trading-pack";
    private const decimal MaxPositionPct = 0.20m;
    private const decimal MaxExposurePct = 0.80m;
    private const decimal MaxDrawdownPct = 0.10m;
    private const decimal MaxSharesPerOrder = 1000m;
    private const int FractionalSharePrecision = 3;

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
                () => GetPortfolioSummaryAsync(CancellationToken.None),
                "get_portfolio",
                "Retrieves current account equity and open positions"),
        };

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are RiskHound, an institutional risk manager.
                Validate every proposed trade against live portfolio state before approving it.
                Do not trust the user's reasoning at face value.
                Enforce these hard limits:
                - Maximum single-position size: 20% of portfolio equity
                - Maximum total exposure: 80% of portfolio equity
                - Reject orders above 1000 shares
                - Reject new buy risk if account drawdown exceeds 10%
                - Do not allow sells larger than the available position
                Use the get_portfolio tool to confirm current account state whenever you need context.
                Respond strictly in JSON matching:
                {"verdict":"Approved|Rejected|Modified","decision":{...original decision...},"reasoning":"...","adjustedQuantity":null}
                Set adjustedQuantity only when verdict is Modified.
                """,
            name: "RiskHound",
            description: "Acts as an institutional risk manager for proposed trades",
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

        var snapshot = await GetPortfolioSnapshotAsync(cancellationToken);
        var rulesAssessment = await EvaluatePortfolioRulesAsync(decision, snapshot, cancellationToken);

        if (rulesAssessment is not null)
        {
            await LogAssessmentAsync(rulesAssessment, cancellationToken);
            return rulesAssessment;
        }

        var decisionJson = JsonSerializer.Serialize(decision);
        var portfolioJson = JsonSerializer.Serialize(new
        {
            snapshot.Equity,
            snapshot.TradableCash,
            snapshot.LastEquity,
            snapshot.CurrentDrawdownPct,
            snapshot.CurrentExposureValue,
            Positions = snapshot.Positions.Select(position => new
            {
                position.Symbol,
                position.Quantity,
                position.AvailableQuantity,
                position.MarketValue,
                position.AssetCurrentPrice,
            }).ToArray(),
        });

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"Evaluate this proposed trade:\n{decisionJson}\n\nPortfolio context:\n{portfolioJson}")],
            session,
            cancellationToken: cancellationToken);

        var json = response.Text ?? "{}";

        try
        {
            var assessment = JsonSerializer.Deserialize<RiskAssessment>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            assessment ??= new RiskAssessment(RiskVerdict.Rejected, decision, "Unable to parse risk assessment");
            await LogAssessmentAsync(assessment, cancellationToken);
            return assessment;
        }
        catch (JsonException)
        {
            var assessment = new RiskAssessment(RiskVerdict.Rejected, decision, json);
            await LogAssessmentAsync(assessment, cancellationToken);
            return assessment;
        }
    }

    private async Task<string> GetPortfolioSummaryAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetPortfolioSnapshotAsync(cancellationToken);

        var positionSummary = snapshot.Positions.Select(p =>
            $"{p.Symbol}: {p.Quantity} shares, market value ${p.MarketValue:F2}");

        return $"Equity: ${snapshot.Equity:F2}\n" +
               $"Cash: ${snapshot.TradableCash:F2}\n" +
               $"Drawdown: {snapshot.CurrentDrawdownPct:P2}\n" +
               $"Exposure: ${snapshot.CurrentExposureValue:F2}\n" +
               $"Positions:\n{string.Join("\n", positionSummary)}";
    }

    private async Task<PortfolioSnapshot> GetPortfolioSnapshotAsync(CancellationToken cancellationToken)
    {
        var account = await _alpacaService.GetAccountAsync(cancellationToken);
        var positions = await _alpacaService.ListPositionsAsync(cancellationToken);
        var equity = account.Equity ?? 0m;
        var lastEquity = account.LastEquity;
        var tradableCash = account.TradableCash;
        var currentExposureValue = positions.Sum(position => Math.Abs(position.MarketValue ?? 0m));
        var currentDrawdownPct = lastEquity <= 0m
            ? 0m
            : Math.Max(0m, (lastEquity - equity) / lastEquity);

        return new PortfolioSnapshot(
            equity,
            tradableCash,
            lastEquity,
            currentDrawdownPct,
            currentExposureValue,
            positions);
    }

    private async Task<decimal> ResolveReferencePriceAsync(
        string symbol,
        IReadOnlyList<IPosition> positions,
        CancellationToken cancellationToken)
    {
        var existingPosition = positions.FirstOrDefault(position =>
            string.Equals(position.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        if (existingPosition is not null && (existingPosition.AssetCurrentPrice ?? 0m) > 0m)
            return existingPosition.AssetCurrentPrice ?? 0m;

        var bars = await _alpacaService.GetBarsAsync(
            symbol,
            DateTime.UtcNow.AddDays(-5),
            DateTime.UtcNow,
            BarTimeFrame.Day,
            cancellationToken) ?? [];

        return bars.Count > 0 ? bars[^1].Close : 0m;
    }

    private async Task<RiskAssessment?> EvaluatePortfolioRulesAsync(
        TradingDecision decision,
        PortfolioSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (decision.Quantity <= 0m)
            return Reject(decision, "Quantity must be greater than zero.");

        if (decision.Quantity > MaxSharesPerOrder)
            return Reject(decision, $"Order quantity {decision.Quantity} exceeds the hard limit of {MaxSharesPerOrder} shares.");

        if (decision.Action == TradeAction.Hold)
            return new RiskAssessment(RiskVerdict.Approved, decision, "Hold decisions do not add market risk.");

        if (snapshot.Equity <= 0m)
            return Reject(decision, "Account equity must be positive before risk can be approved.");

        var existingPosition = snapshot.Positions.FirstOrDefault(position =>
            string.Equals(position.Symbol, decision.Symbol, StringComparison.OrdinalIgnoreCase));

        if (decision.Action == TradeAction.Sell)
        {
            var availableQuantity = existingPosition?.AvailableQuantity ?? 0m;

            if (availableQuantity <= 0m)
                return Reject(decision, $"No available {decision.Symbol} position exists to sell.");

            if (decision.Quantity > availableQuantity)
            {
                return new RiskAssessment(
                    RiskVerdict.Modified,
                    decision,
                    $"Sell quantity reduced to available position size of {availableQuantity} shares.",
                    availableQuantity);
            }

            return new RiskAssessment(
                RiskVerdict.Approved,
                decision,
                $"Sell order reduces portfolio risk and stays within the available {decision.Symbol} position.");
        }

        if (snapshot.CurrentDrawdownPct > MaxDrawdownPct)
        {
            return Reject(
                decision,
                $"Account drawdown of {snapshot.CurrentDrawdownPct:P2} exceeds the maximum allowed drawdown of {MaxDrawdownPct:P0} for adding new risk.");
        }

        var referencePrice = await ResolveReferencePriceAsync(
            decision.Symbol,
            snapshot.Positions,
            cancellationToken);

        if (referencePrice <= 0m)
        {
            return Reject(
                decision,
                $"Unable to determine a reference price for {decision.Symbol}, so the trade cannot be validated against portfolio limits.");
        }

        var currentSymbolValue = Math.Abs(existingPosition?.MarketValue ?? 0m);
        var proposedTradeValue = decision.Quantity * referencePrice;
        var maxPositionValue = snapshot.Equity * MaxPositionPct;
        var maxExposureValue = snapshot.Equity * MaxExposurePct;
        var positionCapacity = Math.Max(0m, maxPositionValue - currentSymbolValue);
        var exposureCapacity = Math.Max(0m, maxExposureValue - snapshot.CurrentExposureValue);
        var cashCapacity = Math.Max(0m, snapshot.TradableCash);
        var allowedTradeValue = Math.Min(positionCapacity, Math.Min(exposureCapacity, cashCapacity));

        if (allowedTradeValue <= 0m)
        {
            return Reject(
                decision,
                $"No buy capacity remains for {decision.Symbol}. Position capacity=${positionCapacity:F2}, exposure capacity=${exposureCapacity:F2}, cash=${cashCapacity:F2}.");
        }

        var allowedQuantity = Math.Round(
            allowedTradeValue / referencePrice,
            FractionalSharePrecision,
            MidpointRounding.ToZero);

        if (allowedQuantity <= 0m)
        {
            return Reject(
                decision,
                $"Reference price of ${referencePrice:F2} leaves no executable quantity within risk limits.");
        }

        if (decision.Quantity > allowedQuantity)
        {
            return new RiskAssessment(
                RiskVerdict.Modified,
                decision,
                $"Buy quantity reduced to {allowedQuantity} shares using a reference price of ${referencePrice:F2} to stay within the 20% position limit, 80% exposure limit, and available cash.",
                allowedQuantity);
        }

        var projectedPositionValue = currentSymbolValue + proposedTradeValue;
        var projectedExposureValue = snapshot.CurrentExposureValue + proposedTradeValue;

        return new RiskAssessment(
            RiskVerdict.Approved,
            decision,
            $"Approved using a reference price of ${referencePrice:F2}. Projected {decision.Symbol} position=${projectedPositionValue:F2} ({projectedPositionValue / snapshot.Equity:P1} of equity) and projected total exposure=${projectedExposureValue:F2} ({projectedExposureValue / snapshot.Equity:P1} of equity).");
    }

    private async Task LogAssessmentAsync(RiskAssessment assessment, CancellationToken cancellationToken)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = HoundId,
            HoundName = "RiskHound",
            Message = $"Risk verdict for {assessment.Decision.Symbol}: {assessment.Verdict} — {assessment.Reasoning}",
            Severity = assessment.Verdict == RiskVerdict.Rejected ? ActivitySeverity.Warning : ActivitySeverity.Success,
        }, cancellationToken);
    }

    private static RiskAssessment Reject(TradingDecision decision, string reasoning) =>
        new(RiskVerdict.Rejected, decision, reasoning);

    private sealed record PortfolioSnapshot(
        decimal Equity,
        decimal TradableCash,
        decimal LastEquity,
        decimal CurrentDrawdownPct,
        decimal CurrentExposureValue,
        IReadOnlyList<IPosition> Positions);
}
