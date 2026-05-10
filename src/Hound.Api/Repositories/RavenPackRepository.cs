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

    public async Task RegisterPackAsync(PackRegistration registration, CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession();

        var pack = await session.LoadAsync<Pack>(registration.Id, cancellationToken)
            ?? new Pack { Id = registration.Id };

        pack.Name = registration.Name;
        pack.HoundCount = registration.Hounds.Count;
        pack.HoundIds = registration.Hounds.Select(h => h.Id).ToList();
        pack.Status = PackStatus.Running;

        await session.StoreAsync(pack, cancellationToken);

        foreach (var hound in registration.Hounds)
        {
            var houndInfo = await session.LoadAsync<HoundInfo>(hound.Id, cancellationToken)
                ?? new HoundInfo { Id = hound.Id };

            houndInfo.Name = hound.Name;
            houndInfo.PackId = registration.Id;
            houndInfo.Status = HoundStatus.Idle;

            await session.StoreAsync(houndInfo, cancellationToken);
        }

        await session.SaveChangesAsync(cancellationToken);
    }
}
