using Alpaca.Markets;

namespace Hound.Trading.AlpacaClient;

public interface IAlpacaService
{
    Task<IAccount> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPosition>> ListPositionsAsync(CancellationToken cancellationToken = default);
    Task<IOrder> SubmitOrderAsync(string symbol, OrderQuantity quantity, OrderSide side, OrderType type, TimeInForce timeInForce, decimal? limitPrice = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IBar>> GetBarsAsync(string symbol, DateTime from, DateTime to, BarTimeFrame timeFrame, CancellationToken cancellationToken = default);
}

public class AlpacaService : IAlpacaService
{
    // TODO: Implement in Wave 2 — wrap Alpaca.Markets SDK
    public Task<IAccount> GetAccountAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<IPosition>> ListPositionsAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IOrder> SubmitOrderAsync(string symbol, OrderQuantity quantity, OrderSide side, OrderType type, TimeInForce timeInForce, decimal? limitPrice = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<IBar>> GetBarsAsync(string symbol, DateTime from, DateTime to, BarTimeFrame timeFrame, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
