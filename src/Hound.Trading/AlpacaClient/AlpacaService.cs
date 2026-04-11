using Alpaca.Markets;
using Microsoft.Extensions.Options;
using AlpacaEnvironments = Alpaca.Markets.Environments;

namespace Hound.Trading.AlpacaClient;

public interface IAlpacaService
{
    Task<IAccount> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPosition>> ListPositionsAsync(CancellationToken cancellationToken = default);
    Task<IOrder> SubmitOrderAsync(string symbol, OrderQuantity quantity, OrderSide side, OrderType type, TimeInForce timeInForce, decimal? limitPrice = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IBar>> GetBarsAsync(string symbol, DateTime from, DateTime to, BarTimeFrame timeFrame, CancellationToken cancellationToken = default);
}

public class AlpacaService : IAlpacaService, IDisposable
{
    private readonly IAlpacaTradingClient _tradingClient;
    private readonly IAlpacaDataClient _dataClient;

    public AlpacaService(IOptions<AlpacaSettings> options)
    {
        var settings = options.Value;
        var secretKey = new SecretKey(settings.ApiKeyId, settings.SecretKey);

        var environment = string.Equals(settings.Environment, "Live", StringComparison.OrdinalIgnoreCase)
            ? AlpacaEnvironments.Live
            : AlpacaEnvironments.Paper;

        _tradingClient = environment.GetAlpacaTradingClient(secretKey);
        _dataClient = environment.GetAlpacaDataClient(secretKey);
    }

    public Task<IAccount> GetAccountAsync(CancellationToken cancellationToken = default)
        => _tradingClient.GetAccountAsync(cancellationToken);

    public Task<IReadOnlyList<IPosition>> ListPositionsAsync(CancellationToken cancellationToken = default)
        => _tradingClient.ListPositionsAsync(cancellationToken);

    public async Task<IOrder> SubmitOrderAsync(
        string symbol,
        OrderQuantity quantity,
        OrderSide side,
        OrderType type,
        TimeInForce timeInForce,
        decimal? limitPrice = null,
        CancellationToken cancellationToken = default)
    {
        var request = new NewOrderRequest(symbol, quantity, side, type, timeInForce);
        if (limitPrice.HasValue)
            request.LimitPrice = limitPrice.Value;

        return await _tradingClient.PostOrderAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<IBar>> GetBarsAsync(
        string symbol,
        DateTime from,
        DateTime to,
        BarTimeFrame timeFrame,
        CancellationToken cancellationToken = default)
    {
        var request = new HistoricalBarsRequest(symbol, from, to, timeFrame);
        var page = await _dataClient.GetHistoricalBarsAsync(request, cancellationToken);
        return page.Items.TryGetValue(symbol, out var bars) ? bars : [];
    }

    public void Dispose()
    {
        _tradingClient.Dispose();
        _dataClient.Dispose();
    }
}
