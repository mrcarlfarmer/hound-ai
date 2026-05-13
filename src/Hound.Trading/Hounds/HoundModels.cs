namespace Hound.Trading.Hounds;

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

public record WorldState(
    string Symbol,
    decimal LastPrice,
    decimal VolumeChange,
    string Trend,
    double ConfidenceScore,
    string Summary,
    Dictionary<string, object>? Indicators = null,
    DateTime? CapturedAtUtc = null);

public record ProposedTradeSignal(
    string Symbol,
    TradeAction Action,
    decimal Quantity,
    string Reasoning,
    double Confidence,
    string Status,
    string SourceHound,
    DateTime CreatedAtUtc,
    WorldState WorldState)
{
    public string Id { get; init; } = string.Empty;
}

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
