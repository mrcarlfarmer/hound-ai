using Hound.Core.Models;
using Raven.Client.Documents;

namespace Hound.Core.Logging;

public class RavenActivityLogger : IActivityLogger
{
    private readonly IDocumentStore _store;

    public RavenActivityLogger(IDocumentStore store)
    {
        _store = store;
    }

    public Task LogActivityAsync(ActivityLog activity, CancellationToken cancellationToken = default)
    {
        // TODO: Implement in Wave 2 — store ActivityLog to pack-specific database
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ActivityLog>> GetActivitiesAsync(
        string? packId = null,
        string? houndId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement in Wave 2 — query with filters, pagination
        throw new NotImplementedException();
    }
}
