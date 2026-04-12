using Hound.Core.Models;

namespace Hound.Api.Repositories;

public interface ITunerExperimentRepository
{
    Task<IReadOnlyList<TunerExperiment>> GetExperimentsAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<TunerExperiment?> GetExperimentAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(
        string id,
        TunerExperimentStatus status,
        CancellationToken cancellationToken = default);
}
