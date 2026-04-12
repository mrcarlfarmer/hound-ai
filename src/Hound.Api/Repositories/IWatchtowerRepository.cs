using Hound.Core.Models;

namespace Hound.Api.Repositories;

public interface IWatchtowerRepository
{
    Task StoreEventAsync(WatchtowerEvent evt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WatchtowerEvent>> GetEventsAsync(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
}
