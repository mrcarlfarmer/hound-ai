using Hound.Core.Logging;
using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Hound.Api.Services;

public class RavenActivityService : IActivityLogger
{
    private readonly IDocumentStore _store;

    public RavenActivityService(IDocumentStore store)
    {
        _store = store;
    }

    public async Task LogActivityAsync(ActivityLog activity, CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession();
        await session.StoreAsync(activity, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityLog>> GetActivitiesAsync(
        string? packId = null,
        string? houndId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        using var session = _store.OpenAsyncSession();
        var query = session.Query<ActivityLog>();

        if (!string.IsNullOrEmpty(packId))
            query = query.Where(a => a.PackId == packId);

        if (!string.IsNullOrEmpty(houndId))
            query = query.Where(a => a.HoundId == houndId);

        if (from.HasValue)
            query = query.Where(a => a.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(a => a.Timestamp <= to.Value);

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }
}
