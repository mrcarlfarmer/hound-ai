namespace Hound.Core.Models;

/// <summary>Status of a queued graph run request.</summary>
public enum RunRequestStatus { Pending, Running, Completed, Failed }

/// <summary>
/// A request to run the trading graph for a specific symbol.
/// Written by the API, consumed by the trading-pack worker.
/// Stored in the <c>hound-trading-pack</c> database, <c>RunRequests</c> collection.
/// </summary>
public class RunRequest
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public RunRequestStatus Status { get; set; } = RunRequestStatus.Pending;
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? RunId { get; set; }
    public string? ErrorMessage { get; set; }
}
