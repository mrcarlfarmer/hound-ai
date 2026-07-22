namespace Hound.Core.LlmClient;

/// <summary>
/// Ambient context carrying the current graph run + node identifiers so that
/// <see cref="StreamingChatClient"/> can attribute streamed LLM output to the
/// correct node without modifying every node implementation.
/// </summary>
public sealed class NodeStreamContext
{
    private static readonly AsyncLocal<NodeStreamContext?> _current = new();

    public string RunId { get; }
    public string NodeId { get; }
    public INodeStreamPublisher Publisher { get; }

    private NodeStreamContext(string runId, string nodeId, INodeStreamPublisher publisher)
    {
        RunId = runId;
        NodeId = nodeId;
        Publisher = publisher;
    }

    public static NodeStreamContext? Current => _current.Value;

    /// <summary>
    /// Begin a scope for the given node execution. Dispose to restore the previous scope.
    /// </summary>
    public static IDisposable Begin(string runId, string nodeId, INodeStreamPublisher publisher)
    {
        var previous = _current.Value;
        _current.Value = new NodeStreamContext(runId, nodeId, publisher);
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly NodeStreamContext? _previous;
        private bool _disposed;

        public Scope(NodeStreamContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
