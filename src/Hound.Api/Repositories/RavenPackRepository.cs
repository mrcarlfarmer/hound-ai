using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Hound.Api.Repositories;

public class RavenPackRepository : IPackRepository
{
    private readonly IDocumentStore _store;

    public RavenPackRepository(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<Pack>> GetAllPacksAsync(CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession();
        return await session.Query<Pack>().ToListAsync(cancellationToken);
    }

    public async Task<Pack?> GetPackAsync(string id, CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession();
        return await session.LoadAsync<Pack>(id, cancellationToken);
    }

    public async Task<IReadOnlyList<HoundInfo>> GetHoundsAsync(string packId, CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession();
        return await session.Query<HoundInfo>()
            .Where(h => h.PackId == packId)
            .ToListAsync(cancellationToken);
    }
}
