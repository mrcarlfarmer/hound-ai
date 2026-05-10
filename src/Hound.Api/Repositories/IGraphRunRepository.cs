using Hound.Core.Models;

namespace Hound.Api.Repositories;

public interface IGraphRunRepository
{
    Task<IReadOnlyList<GraphRun>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken = default);
    Task<GraphRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<RunRequest> QueueRunRequestAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunRequest>> GetRecentRequestsAsync(int limit, CancellationToken cancellationToken = default);
}
