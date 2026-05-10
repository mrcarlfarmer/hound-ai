using Hound.Core.Models;

namespace Hound.Api.Repositories;

public interface ITradeRepository
{
    Task<IReadOnlyList<TradeDocument>> GetTradesAsync(
        int page = 1,
        int pageSize = 20,
        string? symbol = null,
        FillStatus? fillStatus = null,
        CancellationToken cancellationToken = default);

    Task<TradeDocument?> GetTradeAsync(
        string id,
        CancellationToken cancellationToken = default);

    Task UpsertTradeAsync(
        TradeDocument trade,
        CancellationToken cancellationToken = default);
}
