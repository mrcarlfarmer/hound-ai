namespace Hound.Core.Models;

/// <summary>
/// Incremental output emitted by a node's LLM call while it is executing.
/// Broadcast to the dashboard so users can watch reasoning unfold in real time.
/// </summary>
public class NodeStreamChunk
{
    public string PackId { get; set; } = "trading-pack";
    public string RunId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
