using Alpaca.Markets;
using Microsoft.Extensions.Options;
using AlpacaEnvironments = Alpaca.Markets.Environments;

namespace Hound.Trading.AlpacaClient;

public interface IAlpacaService
{
    Task<IAccount> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPosition>> ListPositionsAsync(CancellationToken cancellationToken = default);
    Task<IOrder> SubmitOrderAsync(string symbol, OrderQuantity quantity, OrderSide side, OrderType type, TimeInForce timeInForce, decimal? limitPrice = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a trailing-stop order with the supplied trail offset (as a
    /// percentage of the high-water mark). Used by <c>ExecutionNode</c> for
    /// every Sell so exits ride the position up and only fire after a
    /// pullback of <paramref name="trailPercent"/>%. <paramref name="timeInForce"/>
    /// is expected to be <see cref="TimeInForce.Gtc"/> in production.
    /// </summary>
    Task<IOrder> SubmitTrailingStopOrderAsync(
        string symbol,
        OrderQuantity quantity,
        OrderSide side,
        decimal trailPercent,
        TimeInForce timeInForce,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IBar>> GetBarsAsync(string symbol, DateTime from, DateTime to, BarTimeFrame timeFrame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the latest trade (last print) for a symbol via the Alpaca Data
    /// API. Used by the <c>SoftwareStopPoller</c> to evaluate software-emulated
    /// trailing stops without any LLM involvement. Returns <c>null</c> when the
    /// broker errors or has no recent trade so callers can degrade gracefully.
    /// </summary>
    Task<ITrade?> GetLatestTradeAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the broker's market clock (open/close state and next session
    /// boundaries). Used to gate the software-stop poller so it only runs
    /// while the market is open.
    /// </summary>
    Task<IClock> GetClockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a ticker symbol to the broker's canonical asset record
    /// (legal company name, exchange, tradability). Returns <c>null</c> when
    /// the symbol is unknown or the lookup fails.
    /// </summary>
    Task<IAsset?> GetAssetAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IOrder> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IOrder>> ListOrdersAsync(OrderStatusFilter? statusFilter = null, CancellationToken cancellationToken = default);
    Task<bool> CancelOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists historical news articles for one or more symbols via the Alpaca
    /// Data API. Returns an empty list when the broker responds with an error
    /// or no articles match — callers must treat news as best-effort.
    /// </summary>
    Task<IReadOnlyList<INewsArticle>> ListNewsAsync(
        IReadOnlyCollection<string> symbols,
        DateTime since,
        int maxItems,
        CancellationToken cancellationToken = default);
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

    public async Task<IOrder> SubmitTrailingStopOrderAsync(
        string symbol,
        OrderQuantity quantity,
        OrderSide side,
        decimal trailPercent,
        TimeInForce timeInForce,
        CancellationToken cancellationToken = default)
    {
        if (trailPercent <= 0m)
            throw new ArgumentOutOfRangeException(nameof(trailPercent), trailPercent, "Trail percent must be greater than 0.");

        // Alpaca only accepts trailing-stop (and any stop variant) orders for
        // whole-share quantities. Fractional positions are categorically
        // unsupported, so fail fast with a clear message instead of letting
        // the broker reject us with the generic "fractional orders must be
        // DAY orders" error \u2014 which is doubly misleading because changing TIF
        // doesn't help either.
        if (quantity.IsInDollars || quantity.Value != Math.Truncate(quantity.Value))
            throw new InvalidOperationException(
                $"Alpaca does not support trailing-stop orders on fractional quantities (requested {quantity.Value} {symbol}). " +
                "Round the position to whole shares before attaching a protective stop.");

        var request = new NewOrderRequest(symbol, quantity, side, OrderType.TrailingStop, timeInForce)
        {
            TrailOffsetInPercent = trailPercent,
        };

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

    public async Task<ITrade?> GetLatestTradeAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new LatestMarketDataRequest(symbol);
            return await _dataClient.GetLatestTradeAsync(request, cancellationToken);
        }
        catch
        {
            // Latest-trade lookups feed the software-stop poller, which must
            // degrade gracefully — a transient data-API failure should skip
            // this symbol for the current cycle, not crash the poller.
            return null;
        }
    }

    public Task<IClock> GetClockAsync(CancellationToken cancellationToken = default)
        => _tradingClient.GetClockAsync(cancellationToken);

    public async Task<IAsset?> GetAssetAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _tradingClient.GetAssetAsync(symbol, cancellationToken);
        }
        catch
        {
            // Unknown symbol or transient broker error — treat as "no asset"
            // so callers can fall back to the raw ticker.
            return null;
        }
    }

    public Task<IOrder> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => _tradingClient.GetOrderAsync(orderId, cancellationToken);

    public async Task<IReadOnlyList<IOrder>> ListOrdersAsync(
        OrderStatusFilter? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ListOrdersRequest();
        if (statusFilter.HasValue)
            request.OrderStatusFilter = statusFilter.Value;

        return await _tradingClient.ListOrdersAsync(request, cancellationToken);
    }

    public async Task<bool> CancelOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return await _tradingClient.CancelOrderAsync(orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<INewsArticle>> ListNewsAsync(
        IReadOnlyCollection<string> symbols,
        DateTime since,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0 || maxItems <= 0)
            return [];

        try
        {
            var request = new NewsArticlesRequest(symbols)
            {
                TimeInterval = new Interval<DateTime>(since, DateTime.UtcNow),
                SortDirection = SortDirection.Descending,
                ExcludeItemsWithoutContent = false,
                SendFullContentForItems = false,
            };

            // Alpaca caps the news page size at 50; honour that cap so we
            // don't trigger a 422 response for over-large page sizes.
            const int alpacaMaxNewsPageSize = 50;
            var pageSize = Math.Min(maxItems, alpacaMaxNewsPageSize);
            request.Pagination.Size = (uint)pageSize;

            var page = await _dataClient.ListNewsArticlesAsync(request, cancellationToken);
            return page.Items.Take(maxItems).ToList();
        }
        catch
        {
            // Treat any broker failure as "no news available" — news is a
            // best-effort signal and must never break the analyst pipeline.
            return [];
        }
    }

    public void Dispose()
    {
        _tradingClient.Dispose();
        _dataClient.Dispose();
    }
}
