using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Hound.Api.Repositories;

public class RavenGraphRunRepository : IGraphRunRepository
{
    private const string Database = "hound-trading-pack";

    private readonly IDocumentStore _store;

    public RavenGraphRunRepository(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<GraphRun>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession(Database);
        return await session.Query<GraphRun>()
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<GraphRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession(Database);
        return await session.LoadAsync<GraphRun>($"GraphRuns/{runId}", cancellationToken);
    }

    public async Task<RunRequest> QueueRunRequestAsync(string symbol, CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession(Database);
        var request = new RunRequest
        {
            Symbol = symbol,
            Status = RunRequestStatus.Pending,
            RequestedAt = DateTime.UtcNow,
        };
        await session.StoreAsync(request, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
        return request;
    }

    public async Task<IReadOnlyList<RunRequest>> GetRecentRequestsAsync(int limit, CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession(Database);
        return await session.Query<RunRequest>()
            .OrderByDescending(r => r.RequestedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
