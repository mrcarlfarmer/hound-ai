using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Hound.Api.Repositories;

public class RavenTunerExperimentRepository : ITunerExperimentRepository
{
    private const string TunerDatabase = "hound-trading-pack";

    private readonly IDocumentStore _store;

    public RavenTunerExperimentRepository(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<TunerExperiment>> GetExperimentsAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        using var session = _store.OpenAsyncSession(TunerDatabase);
        return await session.Query<TunerExperiment>()
            .OrderByDescending(e => e.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<TunerExperiment?> GetExperimentAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession(TunerDatabase);
        return await session.LoadAsync<TunerExperiment>(id, cancellationToken);
    }

    public async Task UpdateStatusAsync(
        string id,
        TunerExperimentStatus status,
        CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession(TunerDatabase);
        var experiment = await session.LoadAsync<TunerExperiment>(id, cancellationToken);
        if (experiment is null) return;

        experiment.Status = status;
        await session.SaveChangesAsync(cancellationToken);
    }
}
