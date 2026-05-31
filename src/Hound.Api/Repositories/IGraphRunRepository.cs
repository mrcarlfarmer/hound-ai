using Hound.Core.Models;

namespace Hound.Api.Repositories;

public interface IGraphRunRepository
{
    Task<IReadOnlyList<GraphRun>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken = default);
    Task<GraphRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default);
    Task<RunRequest> QueueRunRequestAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunRequest>> GetRecentRequestsAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a human approval/rejection for a graph run. Writes a
    /// <see cref="GraphApproval"/> document that the trading worker polls
    /// and applies. Returns false if the run does not exist or is not
    /// currently awaiting approval.
    /// </summary>
    Task<bool> SubmitApprovalAsync(
        string runId,
        ApprovalStatus decision,
        string? decidedBy,
        string? notes,
        CancellationToken cancellationToken = default);
}
