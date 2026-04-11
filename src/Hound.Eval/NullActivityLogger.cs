using Hound.Core.Logging;
using Hound.Core.Models;

namespace Hound.Eval;

/// <summary>
/// No-op <see cref="IActivityLogger"/> used by the eval harness to discard hound logs.
/// </summary>
internal sealed class NullActivityLogger : IActivityLogger
{
    public Task LogActivityAsync(ActivityLog activity, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ActivityLog>> GetActivitiesAsync(
        string? packId = null,
        string? houndId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ActivityLog>>(Array.Empty<ActivityLog>());
}
