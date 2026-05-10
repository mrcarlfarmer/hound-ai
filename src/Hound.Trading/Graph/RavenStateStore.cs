using Raven.Client.Documents;

namespace Hound.Trading.Graph;

/// <summary>
/// RavenDB-backed checkpointer. Stores <see cref="TradingGraphState"/> documents
/// in the <c>GraphCheckpoints</c> collection of the trading-pack database.
/// </summary>
public class RavenStateStore : IStateStore
{
    private const string Database = "hound-trading-pack";

    private readonly IDocumentStore _documentStore;

    public RavenStateStore(IDocumentStore documentStore)
    {
        _documentStore = documentStore;
    }

    public async Task SaveAsync(TradingGraphState state, CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(Database);
        await session.StoreAsync(state, $"GraphCheckpoints/{state.RunId}", cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task<TradingGraphState?> LoadAsync(string runId, CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(Database);
        return await session.LoadAsync<TradingGraphState>($"GraphCheckpoints/{runId}", cancellationToken);
    }

    public async Task ClearAsync(string runId, CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(Database);
        session.Delete($"GraphCheckpoints/{runId}");
        await session.SaveChangesAsync(cancellationToken);
    }
}
