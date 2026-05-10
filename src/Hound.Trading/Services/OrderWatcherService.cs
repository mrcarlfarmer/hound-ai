using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Hound.Trading.Services;

/// <summary>
/// Background service that polls Alpaca for order status updates on pending/partially-filled trades.
/// Updates <see cref="TradeDocument"/> in RavenDB and logs status changes via <see cref="IActivityLogger"/>.
/// </summary>
public class OrderWatcherService : BackgroundService
{
    private const string Database = "hound-trading-pack";

    private readonly IAlpacaService _alpacaService;
    private readonly IDocumentStore _documentStore;
    private readonly IActivityLogger _activityLogger;
    private readonly ExecutionHoundConfig _config;
    private readonly ILogger<OrderWatcherService> _logger;

    public OrderWatcherService(
        IAlpacaService alpacaService,
        IDocumentStore documentStore,
        IActivityLogger activityLogger,
        IOptions<ExecutionHoundConfig> config,
        ILogger<OrderWatcherService> logger)
    {
        _alpacaService = alpacaService;
        _documentStore = documentStore;
        _activityLogger = activityLogger;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_config.OrderWatchIntervalSeconds);
        var timeout = TimeSpan.FromMinutes(_config.OrderWatchTimeoutMinutes);

        _logger.LogInformation(
            "OrderWatcherService starting. Poll interval: {Interval}s, Timeout: {Timeout} min",
            _config.OrderWatchIntervalSeconds, _config.OrderWatchTimeoutMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollPendingTradesAsync(timeout, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrderWatcherService encountered an error during poll cycle");
            }

            await Task.Delay(interval, stoppingToken);
        }

        _logger.LogInformation("OrderWatcherService stopped");
    }

    private async Task PollPendingTradesAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        List<TradeDocument> pendingTrades;

        using (var session = _documentStore.OpenAsyncSession(Database))
        {
            pendingTrades = await session.Query<TradeDocument>()
                .Where(t => t.FillStatus == FillStatus.Pending || t.FillStatus == FillStatus.PartiallyFilled)
                .ToListAsync(cancellationToken);
        }

        if (pendingTrades.Count == 0)
            return;

        _logger.LogDebug("OrderWatcherService polling {Count} pending trades", pendingTrades.Count);

        foreach (var trade in pendingTrades)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Time out stale trades
            if (DateTime.UtcNow - trade.CreatedAt > timeout)
            {
                await UpdateTradeStatusAsync(trade.Id, FillStatus.Expired, 0, null, cancellationToken);
                continue;
            }

            if (string.IsNullOrEmpty(trade.OrderId))
                continue;

            if (!Guid.TryParse(trade.OrderId, out var orderId))
                continue;

            try
            {
                var order = await _alpacaService.GetOrderAsync(orderId, cancellationToken);
                var newStatus = MapOrderStatus(order.OrderStatus);
                var filledQty = order.FilledQuantity;
                var avgPrice = order.AverageFillPrice;

                if (newStatus == trade.FillStatus && filledQty == trade.FilledQuantity)
                    continue;

                await UpdateTradeStatusAsync(trade.Id, newStatus, filledQty, avgPrice, cancellationToken);

                await _activityLogger.LogActivityAsync(new ActivityLog
                {
                    PackId = "trading-pack",
                    HoundId = "execution-hound",
                    HoundName = "ExecutionHound",
                    Message = $"Order {trade.OrderId} for {trade.Symbol}: {newStatus} — filled {filledQty}/{trade.RequestedQuantity}" +
                              (avgPrice.HasValue ? $" @ ${avgPrice.Value:F2}" : string.Empty),
                    Severity = newStatus == FillStatus.Filled ? ActivitySeverity.Success
                             : newStatus == FillStatus.Canceled ? ActivitySeverity.Warning
                             : ActivitySeverity.Info,
                    Metadata = new Dictionary<string, object>
                    {
                        ["tradeDocumentId"] = trade.Id,
                        ["fillStatus"] = newStatus.ToString(),
                        ["filledQuantity"] = filledQty,
                        ["averageFillPrice"] = avgPrice ?? 0m,
                        ["orderId"] = trade.OrderId,
                    },
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll order {OrderId} for trade {TradeId}",
                    trade.OrderId, trade.Id);
            }
        }
    }

    private async Task UpdateTradeStatusAsync(
        string tradeId,
        FillStatus newStatus,
        decimal filledQuantity,
        decimal? averageFillPrice,
        CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(Database);
        var doc = await session.LoadAsync<TradeDocument>(tradeId, cancellationToken);
        if (doc is null) return;

        doc.FillStatus = newStatus;
        doc.FilledQuantity = filledQuantity;
        doc.AverageFillPrice = averageFillPrice;
        doc.UpdatedAt = DateTime.UtcNow;

        if (newStatus == FillStatus.Filled)
            doc.ExecutionTime = DateTime.UtcNow;

        await session.SaveChangesAsync(cancellationToken);
    }

    public static FillStatus MapOrderStatus(OrderStatus orderStatus) => orderStatus switch
    {
        OrderStatus.New => FillStatus.Pending,
        OrderStatus.Accepted => FillStatus.Pending,
        OrderStatus.PendingNew => FillStatus.Pending,
        OrderStatus.PartiallyFilled => FillStatus.PartiallyFilled,
        OrderStatus.Filled => FillStatus.Filled,
        OrderStatus.Canceled => FillStatus.Canceled,
        OrderStatus.Expired => FillStatus.Expired,
        OrderStatus.Rejected => FillStatus.Rejected,
        _ => FillStatus.Pending,
    };
}
