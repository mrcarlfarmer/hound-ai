using Hound.Core.Models;

namespace Hound.Api.Repositories;

/// <summary>
/// Reads persisted <see cref="DebateRecord"/> documents written once per
/// StrategyNode invocation. Backs the dashboard "Strategy Debate" panel and
/// the <c>GET /api/debates/{runId}</c> endpoint.
/// </summary>
public interface IDebateRepository
{
    /// <summary>
    /// Returns every debate transcript captured for the given run, ordered by
    /// <see cref="DebateRecord.RefinementCount"/> ascending (the first
    /// invocation first, followed by any refinement-loop re-runs). Returns an
    /// empty list when the run has no persisted debates.
    /// </summary>
    Task<IReadOnlyList<DebateRecord>> GetDebatesAsync(
        string runId,
        CancellationToken cancellationToken = default);
}
