namespace Hound.Trading.Hounds;

/// <summary>
/// Creates portfolio-manager world state snapshots, normalized decisions, and persisted trade proposals.
/// </summary>
public static class StrategySignalProposalFactory
{
    private const double ConfidenceThreshold = 0.7;
    private const decimal DefaultTargetNotional = 1000m;

    public static WorldState CreateWorldState(MarketAnalysis analysis, DateTime? capturedAtUtc = null) =>
        new(
            analysis.Symbol,
            analysis.LastPrice,
            analysis.VolumeChange,
            analysis.Trend,
            analysis.ConfidenceScore,
            analysis.Summary,
            analysis.Indicators,
            capturedAtUtc ?? DateTime.UtcNow);

    public static TradingDecision CreateDecision(WorldState worldState)
    {
        var confidence = Math.Clamp(worldState.ConfidenceScore, 0, 1);
        var action = confidence >= ConfidenceThreshold
            ? worldState.Trend.Trim().ToLowerInvariant() switch
            {
                "bullish" => TradeAction.Buy,
                "bearish" => TradeAction.Sell,
                _ => TradeAction.Hold
            }
            : TradeAction.Hold;

        var quantity = action == TradeAction.Hold
            ? 0
            : CalculateQuantity(worldState.LastPrice, confidence);

        return new TradingDecision(
            worldState.Symbol,
            action,
            quantity,
            BuildReasoning(worldState, action),
            confidence);
    }

    public static TradingDecision NormalizeDecision(WorldState worldState, TradingDecision? decision)
    {
        var fallback = CreateDecision(worldState);

        if (decision is null)
        {
            return fallback;
        }

        var quantity = decision.Action == TradeAction.Hold
            ? 0
            : decision.Quantity > 0 ? decision.Quantity : fallback.Quantity;
        var reasoning = string.IsNullOrWhiteSpace(decision.Reasoning)
            ? fallback.Reasoning
            : decision.Reasoning.Trim();

        return new TradingDecision(
            worldState.Symbol,
            decision.Action,
            quantity,
            reasoning,
            Math.Clamp(decision.Confidence, 0, 1));
    }

    public static ProposedTradeSignal? CreateProposal(
        WorldState worldState,
        TradingDecision decision,
        DateTime? createdAtUtc = null)
    {
        var normalizedDecision = NormalizeDecision(worldState, decision);

        if (normalizedDecision.Action == TradeAction.Hold)
        {
            return null;
        }

        return new ProposedTradeSignal(
            normalizedDecision.Symbol,
            normalizedDecision.Action,
            normalizedDecision.Quantity,
            normalizedDecision.Reasoning,
            normalizedDecision.Confidence,
            Status: "Proposed",
            SourceHound: nameof(StrategyHound),
            CreatedAtUtc: createdAtUtc ?? DateTime.UtcNow,
            WorldState: worldState)
        {
            Id = $"Hounds/Proposed/{Guid.NewGuid():N}"
        };
    }

    private static decimal CalculateQuantity(decimal lastPrice, double confidence)
    {
        if (lastPrice <= 0)
        {
            return 0;
        }

        var scaledNotional = DefaultTargetNotional * (decimal)Math.Max(confidence, ConfidenceThreshold);
        var quantity = decimal.Round(scaledNotional / lastPrice, 4, MidpointRounding.AwayFromZero);
        return quantity > 0 ? quantity : 1;
    }

    private static string BuildReasoning(WorldState worldState, TradeAction action) =>
        action switch
        {
            TradeAction.Buy =>
                $"Propose adding exposure because {worldState.Symbol} is {worldState.Trend} with {worldState.ConfidenceScore:P0} confidence and volume change of {worldState.VolumeChange:P1}.",
            TradeAction.Sell =>
                $"Propose reducing exposure because {worldState.Symbol} is {worldState.Trend} with {worldState.ConfidenceScore:P0} confidence and volume change of {worldState.VolumeChange:P1}.",
            _ =>
                $"Hold exposure because {worldState.Symbol} does not meet the {ConfidenceThreshold:P0} confidence threshold for a directional proposal."
        };
}
