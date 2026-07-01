using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Hound.Api.Repositories;

/// <summary>
/// RavenDB-backed <see cref="IDebateRepository"/>. Debate records are written by
/// the trading pack's StrategyNode into the <c>hound-trading-pack</c> database,
/// alongside the <see cref="GraphRun"/> documents they relate to.
/// </summary>
public class RavenDebateRepository : IDebateRepository
{
    private const string Database = "hound-trading-pack";

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
        return await session.Query<DebateRecord>()
            .Where(d => d.RunId == runId)
            .OrderBy(d => d.RefinementCount)
            .ToListAsync(cancellationToken);
    }
}
