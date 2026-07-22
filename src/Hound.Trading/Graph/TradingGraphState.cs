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
    public IReadOnlyList<DebateTurn>? StrategyDebate { get; init; }
    public RiskAssessment? RiskOutput { get; init; }
    public ExecutionResult? ExecutionOutput { get; init; }
    public MonitorResult? MonitorOutput { get; init; }

    /// <summary>
    /// OHLCV bars captured during the analysts-team pre-flight step. Carried
    /// through the graph and persisted on the <c>GraphRun</c> snapshot so the
    /// dashboard's Chart tab can replay the exact data the analysts saw.
    /// </summary>
    public ChartSnapshot? ChartSnapshot { get; init; }

    // ── Loop counters ────────────────────────────────────────────────────────
    public int RefinementCount { get; init; }
    public int MonitorCycleCount { get; init; }

    // ── Refinement history ───────────────────────────────────────────────────
    public List<RefinementEntry> RefinementHistory { get; init; } = [];

    // ── Human approval ───────────────────────────────────────────────────────
    /// <summary>Current state of the human-in-the-loop approval gate.</summary>
    public ApprovalStatus ApprovalStatus { get; init; } = ApprovalStatus.NotRequested;
    public string? ApprovalDecidedBy { get; init; }
    public DateTime? ApprovalDecidedAt { get; init; }
    public string? ApprovalNotes { get; init; }
    public DateTime? ApprovalRequestedAt { get; init; }

    // ── Terminal flags ───────────────────────────────────────────────────────
    public bool IsComplete { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Convenience: graph should pause execution and wait for a human.</summary>
    public bool IsAwaitingApproval => ApprovalStatus == ApprovalStatus.Pending && !IsComplete;

    /// <summary>Creates the initial state for a new graph run.</summary>
    public static TradingGraphState Initial(string symbol) => new()
    {
        RunId = $"{symbol}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}",
        Symbol = symbol,
    };
}
