using Hound.Core.Models;

namespace Hound.Trading.Nodes;

public record MarketAnalysis(
    string Symbol,
    decimal LastPrice,
    decimal VolumeChange,
    string Trend,
    double ConfidenceScore,
    string Summary,
    Dictionary<string, object>? Indicators = null);

public enum TradeAction { Buy, Sell, Hold }

public record TradingDecision(
    string Symbol,
    TradeAction Action,
    decimal Quantity,
    string Reasoning,
    double Confidence);

public enum RiskVerdict { Approved, Rejected, Modified }

public record RiskAssessment(
    RiskVerdict Verdict,
    TradingDecision Decision,
    string Reasoning,
    decimal? AdjustedQuantity = null);

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
/// <c>true</c> the graph loops back to DataNode for a refresh cycle.
/// </summary>
public record MonitorResult(
    bool TradeOpen,
    FillStatus CurrentStatus,
    decimal? CurrentPrice,
    decimal? UnrealizedPnL,
    string Summary);
