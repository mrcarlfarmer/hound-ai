using Hound.Core.Models;

namespace Hound.Core.LlmClient;

/// <summary>
/// Publishes incremental node output chunks for live dashboard streaming.
/// Implementations should be fire-and-forget and non-blocking on the hot
/// streaming path. <see cref="FlushAsync"/> drains the publisher so callers
/// can guarantee dashboard ordering between graph nodes.
/// </summary>
public interface INodeStreamPublisher
{
    void Publish(NodeStreamChunk chunk);

    /// <summary>
    /// Returns the accumulated reasoning text streamed so far for the given
    /// node within a run, or <c>null</c> if no chunks have been seen.
    /// </summary>
    string? GetReasoning(string runId, string nodeId);

    /// <summary>
    /// Clears the accumulated reasoning buffer for a single (run, node) pair.
    /// Call this whenever a node is about to be entered so refinement-loop
    /// re-runs don't concatenate their streamed output onto the previous
    /// attempt's text. Default is a no-op for in-memory test doubles.
    /// </summary>
    void ResetReasoning(string runId, string nodeId) { }

    /// <summary>
    /// Awaits until every chunk enqueued prior to this call has been
    /// dispatched. Use between graph nodes so the dashboard renders a node's
    /// full streamed output before the next node starts logging activity.
    /// Default implementation is a no-op for in-memory test doubles.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
