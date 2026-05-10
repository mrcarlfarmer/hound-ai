namespace Hound.Core.Models;

/// <summary>Phase of a graph run.</summary>
public enum GraphPhase { Entry, Monitor }

/// <summary>Status of a single node within a graph run.</summary>
public enum NodeStatus { Pending, Active, Completed, Failed, Skipped }

/// <summary>
/// Snapshot of a trading graph run, persisted in RavenDB and served to the dashboard.
/// The trading-pack writes these documents; the API reads them for the UI.
/// </summary>
public class GraphRun
{
    public string Id { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public GraphPhase Phase { get; set; } = GraphPhase.Entry;
    public string? CurrentNode { get; set; }
    public bool IsComplete { get; set; }
    public string? ErrorMessage { get; set; }
    public int RefinementCount { get; set; }
    public int MonitorCycleCount { get; set; }

    public List<NodeSnapshot> Nodes { get; set; } = [];
}

/// <summary>
/// Snapshot of a single node's state within a graph run.
/// </summary>
public class NodeSnapshot
{
    public string NodeId { get; set; } = string.Empty;
    public NodeStatus Status { get; set; } = NodeStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }
}
