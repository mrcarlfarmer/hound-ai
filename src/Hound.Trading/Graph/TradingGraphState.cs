using Hound.Core.Models;
using Hound.Trading.Nodes;

namespace Hound.Trading.Graph;

/// <summary>
/// Immutable state record that flows through the trading graph.
/// Each node returns <c>state with { ... }</c> to produce the next snapshot.
/// </summary>
public record TradingGraphState
{
    public required string RunId { get; init; }
    public required string Symbol { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public string? CurrentNode { get; init; }
    public GraphPhase Phase { get; init; } = GraphPhase.Entry;

    // ── Node output slots ────────────────────────────────────────────────────
    public MarketAnalysis? DataOutput { get; init; }
    public TradingDecision? StrategyOutput { get; init; }
    public RiskAssessment? RiskOutput { get; init; }
    public ExecutionResult? ExecutionOutput { get; init; }
    public MonitorResult? MonitorOutput { get; init; }

    // ── Loop counters ────────────────────────────────────────────────────────
    public int RefinementCount { get; init; }
    public int MonitorCycleCount { get; init; }

    // ── Terminal flags ───────────────────────────────────────────────────────
    public bool IsComplete { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Creates the initial state for a new graph run.</summary>
    public static TradingGraphState Initial(string symbol) => new()
    {
        RunId = $"{symbol}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}",
        Symbol = symbol,
    };
}
