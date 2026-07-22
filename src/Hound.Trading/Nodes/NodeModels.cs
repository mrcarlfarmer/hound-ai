using Hound.Core.Models;

namespace Hound.Trading.Nodes;

public record MarketAnalysis(
    string Symbol,
    decimal? LastPrice,
    decimal? VolumeChange,
    string Trend,
    double? ConfidenceScore,
    string Summary,
    Dictionary<string, object>? Indicators = null,
    string? MarketReport = null,
    string? FundamentalsReport = null,
    string? NewsReport = null,
    string? SentimentReport = null,
    string? CompanyName = null,
    decimal? Atr14 = null,
    KeyLevels? KeyLevels = null);

/// <summary>
/// Deterministically-derived price levels surfaced to the strategy hound so
/// it can <em>select</em> entry / stop / target from a real menu instead of
/// inventing round-number levels. Always sorted ascending; values are rounded
/// to 2 decimal places. Both lists may be empty when there aren't enough bars
/// to compute them confidently.
/// </summary>
/// <param name="Support">Levels at or below the current price.</param>
/// <param name="Resistance">Levels at or above the current price.</param>
public record KeyLevels(
    IReadOnlyList<decimal> Support,
    IReadOnlyList<decimal> Resistance);

public enum TradeAction { Buy, Sell, Hold }

/// <summary>
/// Strategy hound output. <see cref="TrailPercent"/> is only meaningful for
/// <see cref="TradeAction.Sell"/> orders, which are placed as trailing-stop
/// GTC orders by <c>ExecutionNode</c>. When the strategy omits it (or sets
/// it for a Buy/Hold), the execution layer falls back to the default trail
/// percent (<c>5%</c>).
/// </summary>
public record TradingDecision(
    string Symbol,
    TradeAction Action,
    decimal Quantity,
    string Reasoning,
    double Confidence,
    decimal? CurrentPrice = null,
    decimal? EstimatedCost = null,
    decimal? TrailPercent = null);

// DebateTurn moved to Hound.Core.Models so the persisted GraphRun snapshot can
// reference it without taking a dependency on Hound.Trading. The type alias
// below is no longer needed — callers import Hound.Core.Models directly.

public enum RiskVerdict { Approved, Rejected, Modified }

public record RiskAssessment(
    RiskVerdict Verdict,
    TradingDecision Decision,
    string Reasoning,
    decimal? AdjustedQuantity = null);

/// <summary>
/// Captures a single refinement iteration: the strategy that was rejected and why.
/// </summary>
public record RefinementEntry(
    int Attempt,
    TradingDecision RejectedDecision,
    string RiskReasoning,
    DateTime OccurredAt);

public record ExecutionResult(
    bool Success,
    string Symbol,
    TradeAction Action,
    decimal Quantity,
    decimal? FilledPrice,
    string OrderId,
    string Message,
    string TradeDocumentId = "");

/// <summary>
/// Result produced by <see cref="MonitorNode"/>. When <see cref="TradeOpen"/> is
/// <c>true</c> the graph loops back to AnalystsTeamNode for a refresh cycle.
/// </summary>
public record MonitorResult(
    bool TradeOpen,
    FillStatus CurrentStatus,
    decimal? CurrentPrice,
    decimal? UnrealizedPnL,
    string Summary);
