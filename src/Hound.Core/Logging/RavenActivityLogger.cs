using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Raven.Client.Exceptions.Database;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;

namespace Hound.Core.Logging;

public class RavenActivityLogger : IActivityLogger
{
    private readonly IDocumentStore _store;

    public RavenActivityLogger(IDocumentStore store)
    {
        _store = store;
    }

    private static string GetDatabaseName(string packId) =>
        string.IsNullOrWhiteSpace(packId) ? "hound-activity" : $"hound-{packId.ToLowerInvariant()}";

    private void EnsureDatabaseExists(string database)
    {
        try
        {
            _store.Maintenance.ForDatabase(database).Send(new GetStatisticsOperation());
        }
        catch (DatabaseDoesNotExistException)
        {
            _store.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(database)));
        }
        catch (Exception)
        {
            // Swallow when Maintenance is unavailable (e.g. in tests with mocked IDocumentStore)
        }
    }

    public async Task LogActivityAsync(ActivityLog activity, CancellationToken cancellationToken = default)
    {
        var database = GetDatabaseName(activity.PackId);
        EnsureDatabaseExists(database);
        using IAsyncDocumentSession session = _store.OpenAsyncSession(database);
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
        var database = GetDatabaseName(packId ?? string.Empty);
        using IAsyncDocumentSession session = _store.OpenAsyncSession(database);

        IRavenQueryable<ActivityLog> query = session.Query<ActivityLog>();

        if (!string.IsNullOrWhiteSpace(packId))
            query = query.Where(a => a.PackId == packId);

        if (!string.IsNullOrWhiteSpace(houndId))
            query = query.Where(a => a.HoundId == houndId);

        if (from.HasValue)
        {
            var fromValue = from.Value;
            query = query.Where(a => a.Timestamp >= fromValue);
        }

        if (to.HasValue)
        {
            var toValue = to.Value;
            query = query.Where(a => a.Timestamp <= toValue);
        }

        var results = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return results.AsReadOnly();
    }
}
