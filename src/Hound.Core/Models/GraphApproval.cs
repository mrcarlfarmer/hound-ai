namespace Hound.Core.Models;

/// <summary>
/// Human-in-the-loop approval lifecycle for a graph run.
/// </summary>
public enum ApprovalStatus
{
    /// <summary>Approval has not yet been requested (default).</summary>
    NotRequested = 0,
    /// <summary>The graph has paused and is waiting on a human decision.</summary>
    Pending = 1,
    /// <summary>A human approved the trade; the graph may proceed to execution.</summary>
    Approved = 2,
    /// <summary>A human rejected the trade; the graph terminates without placing an order.</summary>
    Rejected = 3,
}

/// <summary>
/// Decision document written by the API when a user clicks Approve/Reject.
/// Stored in the trading-pack database. The trading worker polls for these,
/// applies them to the matching <c>GraphCheckpoints/{runId}</c>, then deletes
/// the approval document and resumes the run.
/// </summary>
public class GraphApproval
{
    /// <summary>RavenDB id: <c>GraphApprovals/{runId}</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;
    public ApprovalStatus Decision { get; set; }
    public string? DecidedBy { get; set; }
    public string? Notes { get; set; }
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}
