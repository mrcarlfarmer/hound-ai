using Hound.Core.Models;
using Raven.Client.Documents;

namespace Hound.Api.Repositories;

/// <summary>
/// RavenDB-backed <see cref="IDebateRepository"/>. Debate records are written by
/// the trading pack's StrategyNode into the <c>hound-trading-pack</c> database,
/// alongside the <see cref="GraphRun"/> documents they relate to.
/// </summary>
public class RavenDebateRepository : IDebateRepository
{
    private const string Database = "hound-trading-pack";
    private const int PageSize = 128;

    private readonly IDocumentStore _store;

    public RavenDebateRepository(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<DebateRecord>> GetDebatesAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession(Database);
        var records = new List<DebateRecord>();
        while (true)
        {
            var page = (await session.Advanced.LoadStartingWithAsync<DebateRecord>(
                $"DebateRecords/{runId}/",
                matches: null,
                start: records.Count,
                pageSize: PageSize,
                exclude: null,
                startAfter: null,
                token: cancellationToken)).ToList();

            records.AddRange(page);
            if (page.Count < PageSize)
                break;
        }

        return records
            .OrderBy(d => d.RefinementCount)
            .ToList();
    }
}
