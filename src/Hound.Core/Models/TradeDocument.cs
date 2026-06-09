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
/// How a Buy position's protective exit is enforced.
/// </summary>
public enum StopMode
{
    /// <summary>No protective stop is attached (e.g. Sell orders, or pre-feature trades).</summary>
    None,

    /// <summary>
    /// A broker-side Alpaca trailing-stop Sell (GTC) protects the position.
    /// Only valid for whole-share quantities — Alpaca rejects stops on
    /// fractional positions.
    /// </summary>
    BrokerTrailing,

    /// <summary>
    /// A software-emulated trailing stop protects the position. Used for
    /// fractional quantities, where Alpaca won't accept a broker-side stop.
    /// The <c>SoftwareStopPoller</c> advances <see cref="TradeDocument.HighWaterMark"/>
    /// and submits a market Sell when price falls to <see cref="TradeDocument.StopPrice"/>.
    /// </summary>
    SoftwareTrailing,
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

    // ── Protective stop tracking ──────────────────────────────────────────────

    /// <summary>How this position's protective exit is enforced. <see cref="StopMode.None"/> for Sells.</summary>
    public StopMode StopMode { get; set; } = StopMode.None;

    /// <summary>Trail offset as a percentage of the high-water mark (e.g. 5 = 5%).</summary>
    public decimal? TrailPercent { get; set; }

    /// <summary>Average fill price of the entry order; baseline for the software stop.</summary>
    public decimal? EntryPrice { get; set; }

    /// <summary>
    /// Highest observed price since entry, advanced by the <c>SoftwareStopPoller</c>.
    /// Only lowered never — this is what makes the software stop "trailing".
    /// </summary>
    public decimal? HighWaterMark { get; set; }

    /// <summary>
    /// Current software-stop trigger price, derived as
    /// <c>HighWaterMark * (1 - TrailPercent/100)</c>. When the latest trade
    /// price falls to or below this, the poller closes the position.
    /// </summary>
    public decimal? StopPrice { get; set; }

    /// <summary>Broker order ID of the trailing-stop Sell (only set for <see cref="StopMode.BrokerTrailing"/>).</summary>
    public string? StopOrderId { get; set; }

    /// <summary>Timestamp when the software stop fired and submitted a closing Sell.</summary>
    public DateTime? StopTriggeredAt { get; set; }

    /// <summary>Broker order ID of the market Sell submitted when the software stop fired.</summary>
    public string? StopExitOrderId { get; set; }
}

