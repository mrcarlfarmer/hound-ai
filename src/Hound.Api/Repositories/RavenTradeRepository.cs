using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Hound.Api.Repositories;

public class RavenTradeRepository : ITradeRepository
{
    private const string TradeDatabase = "hound-trading-pack";

    private readonly IDocumentStore _store;

    public RavenTradeRepository(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<TradeDocument>> GetTradesAsync(
        int page = 1,
        int pageSize = 20,
        string? symbol = null,
        FillStatus? fillStatus = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        using var session = _store.OpenAsyncSession(TradeDatabase);
        var query = session.Query<TradeDocument>();

        if (!string.IsNullOrWhiteSpace(symbol))
            query = query.Where(t => t.Symbol == symbol);

        if (fillStatus.HasValue)
        {
            var status = fillStatus.Value;
            query = query.Where(t => t.FillStatus == status);
        }

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<TradeDocument?> GetTradeAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession(TradeDatabase);
        return await session.LoadAsync<TradeDocument>(id, cancellationToken);
    }

    public async Task UpsertTradeAsync(
        TradeDocument trade,
        CancellationToken cancellationToken = default)
    {
        using var session = _store.OpenAsyncSession(TradeDatabase);
        await session.StoreAsync(trade, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }
}
