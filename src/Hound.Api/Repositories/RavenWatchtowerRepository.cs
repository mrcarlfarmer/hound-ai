using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Hound.Api.Repositories;

public class RavenWatchtowerRepository : IWatchtowerRepository
{
    private readonly IDocumentStore _store;

    public RavenWatchtowerRepository(IDocumentStore store)
    {
        _store = store;
    }

    public async Task StoreEventAsync(WatchtowerEvent evt, CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession();
        await session.StoreAsync(evt, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WatchtowerEvent>> GetEventsAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        using var session = _store.OpenAsyncSession();
        return await session.Query<WatchtowerEvent>()
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
}
