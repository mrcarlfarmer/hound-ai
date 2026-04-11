using Hound.Core.Models;

namespace Hound.Core.Logging;

public interface IActivityLogger
{
    Task LogActivityAsync(ActivityLog activity, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLog>> GetActivitiesAsync(
        string? packId = null,
        string? houndId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}
