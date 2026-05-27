namespace Hound.Trading.Graph;

/// <summary>
/// Checkpointer for graph state. Persists state between node executions
/// so long-running graphs (especially monitor loops) can resume after restarts.
/// </summary>
public interface IStateStore
{
    Task SaveAsync(TradingGraphState state, CancellationToken cancellationToken);
    Task<TradingGraphState?> LoadAsync(string runId, CancellationToken cancellationToken);
    Task ClearAsync(string runId, CancellationToken cancellationToken);
    /// <summary>Returns all checkpointed runs that have not completed (for resume on startup).</summary>
    Task<IReadOnlyList<TradingGraphState>> ListIncompleteAsync(CancellationToken cancellationToken);
}
