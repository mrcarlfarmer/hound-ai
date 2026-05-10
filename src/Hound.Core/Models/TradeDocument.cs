namespace Hound.Core.Models;

/// <summary>
/// Represents the lifecycle state of a trade order.
/// </summary>
public enum FillStatus
{
    Pending,
    PartiallyFilled,
    Filled,
    Canceled,
    Expired,
    Rejected
}

/// <summary>
/// Persistent RavenDB document tracking the full lifecycle of a trade from placement through fill.
/// Stored in the <c>hound-trading-pack</c> database.
/// </summary>
public class TradeDocument
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public string OrderId { get; set; } = string.Empty;

    public FillStatus FillStatus { get; set; } = FillStatus.Pending;
    public decimal FilledQuantity { get; set; }
    public decimal? AverageFillPrice { get; set; }

    /// <summary>Timestamp when the order was fully filled.</summary>
    public DateTime? ExecutionTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Summary of the risk assessment reasoning that approved this trade.</summary>
    public string RiskAssessmentSummary { get; set; } = string.Empty;

    public string PackId { get; set; } = string.Empty;
    public string HoundId { get; set; } = string.Empty;
}
