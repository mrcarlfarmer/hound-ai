using Hound.Core.Models;

namespace Hound.Api.Repositories;

public interface IPackRepository
{
    Task<IReadOnlyList<Pack>> GetAllPacksAsync(CancellationToken cancellationToken = default);
    Task<Pack?> GetPackAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HoundInfo>> GetHoundsAsync(string packId, CancellationToken cancellationToken = default);
    Task RegisterPackAsync(PackRegistration registration, CancellationToken cancellationToken = default);
}
