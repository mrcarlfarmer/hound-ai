namespace Hound.Trading.Hounds;

/// <summary>
/// Creates portfolio-manager world state snapshots, normalized decisions, and persisted trade proposals.
/// </summary>
public static class StrategySignalProposalFactory
{
    private const double ConfidenceThreshold = 0.7;
    private const decimal DefaultTargetNotional = 1000m;

    /// <summary>
    /// Creates a world-state snapshot from the latest market analysis payload.
    /// </summary>
    /// <param name="analysis">The latest analysis produced for a symbol.</param>
    /// <param name="capturedAtUtc">Optional override for the snapshot timestamp.</param>
    /// <returns>A world-state record that can be passed to the portfolio-manager workflow.</returns>
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

    /// <summary>
    /// Creates a deterministic portfolio-manager proposal decision from a world-state snapshot.
    /// </summary>
    /// <param name="worldState">The market context to evaluate.</param>
    /// <returns>A normalized proposal decision using the configured confidence threshold.</returns>
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

    /// <summary>
    /// Normalizes an LLM-produced decision and falls back to deterministic defaults when needed.
    /// </summary>
    /// <param name="worldState">The market context associated with the decision.</param>
    /// <param name="decision">The optional decision returned by the agent.</param>
    /// <returns>A decision that always uses the world-state symbol plus valid confidence and fallback quantity/reasoning.</returns>
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

    /// <summary>
    /// Creates a persisted trade proposal for actionable decisions.
    /// </summary>
    /// <param name="worldState">The market context that produced the decision.</param>
    /// <param name="decision">The decision to persist as a proposal.</param>
    /// <param name="createdAtUtc">Optional override for the proposal timestamp.</param>
    /// <returns>A proposal document, or <see langword="null"/> when the decision is Hold.</returns>
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
