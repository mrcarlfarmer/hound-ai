using Hound.Core.Models;

namespace Hound.Core.LlmClient;

/// <summary>
/// Publishes incremental node output chunks for live dashboard streaming.
/// Implementations should be fire-and-forget and non-blocking.
/// </summary>
public interface INodeStreamPublisher
{
    void Publish(NodeStreamChunk chunk);

    /// <summary>
    /// Returns the accumulated reasoning text streamed so far for the given
    /// node within a run, or <c>null</c> if no chunks have been seen.
    /// </summary>
    string? GetReasoning(string runId, string nodeId);
}
