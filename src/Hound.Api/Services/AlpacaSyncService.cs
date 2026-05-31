using Alpaca.Markets;
using Hound.Api.Hubs;
using Hound.Api.Repositories;
using Hound.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;

namespace Hound.Api.Services;

public interface IAlpacaSyncService
{
    Task<AlpacaSyncResult> SyncAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an Alpaca → Hound reconciliation pass.
/// </summary>
public record AlpacaSyncResult(
    int Checked,
    int Updated,
    int Imported,
    int Errors,
    DateTime StartedAt,
    DateTime CompletedAt,
    IReadOnlyList<string> ChangedTradeIds);

/// <summary>
/// Pulls authoritative order state from Alpaca and reconciles the local
/// <see cref="TradeDocument"/> store. Broadcasts <c>OnOrderUpdate</c> over
/// SignalR for every trade whose status, filled qty, or avg price changed,
/// and imports any orders that exist on Alpaca but not in Hound (e.g. sells
/// placed directly in the Alpaca web UI).
/// </summary>
public class AlpacaSyncService : IAlpacaSyncService
{
    private const string TradeDatabase = "hound-trading-pack";
    private static readonly TimeSpan ExternalOrderLookback = TimeSpan.FromDays(30);
    private const int ExternalOrderLimit = 500;
    private const string ExternalPackId = "external";
    private const string ExternalHoundId = "external";

    private readonly IDocumentStore _store;
    private readonly IAlpacaPortfolioService _alpaca;
    private readonly IHubContext<ActivityHub> _hub;
    private readonly ILogger<AlpacaSyncService> _logger;

    public AlpacaSyncService(
        IDocumentStore store,
        IAlpacaPortfolioService alpaca,
        IHubContext<ActivityHub> hub,
        ILogger<AlpacaSyncService> logger)
    {
        _store = store;
        _alpaca = alpaca;
        _hub = hub;
        _logger = logger;
    }

    public async Task<AlpacaSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        var checkedCount = 0;
        var updatedCount = 0;
        var importedCount = 0;
        var errors = 0;
        var changedIds = new List<string>();

        // Load every TradeDocument with an OrderId. Reconcile against Alpaca.
        List<TradeDocument> trades;
        using (var readSession = _store.OpenAsyncSession(TradeDatabase))
        {
            trades = await readSession.Query<TradeDocument>()
                .Where(t => t.OrderId != null && t.OrderId != "")
                .OrderByDescending(t => t.CreatedAt)
                .Take(500)
                .ToListAsync(cancellationToken);
        }

        var knownOrderIds = new HashSet<string>(
            trades.Select(t => t.OrderId),
            StringComparer.OrdinalIgnoreCase);

        foreach (var trade in trades)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkedCount++;

            if (!Guid.TryParse(trade.OrderId, out var orderId))
                continue;

            try
            {
                var order = await _alpaca.GetOrderAsync(orderId, cancellationToken);
                if (order is null)
                {
                    // Alpaca no longer knows about this order. Leave the document as-is.
                    continue;
                }

                var newStatus = MapOrderStatus(order.OrderStatus);
                var changed =
                    trade.FillStatus != newStatus ||
                    trade.FilledQuantity != order.FilledQuantity ||
                    trade.AverageFillPrice != order.AverageFillPrice;

                if (!changed) continue;

                using var writeSession = _store.OpenAsyncSession(TradeDatabase);
                var doc = await writeSession.LoadAsync<TradeDocument>(trade.Id, cancellationToken);
                if (doc is null) continue;

                doc.FillStatus = newStatus;
                doc.FilledQuantity = order.FilledQuantity;
                doc.AverageFillPrice = order.AverageFillPrice;
                doc.UpdatedAt = DateTime.UtcNow;
                if (newStatus == FillStatus.Filled && !doc.ExecutionTime.HasValue)
                    doc.ExecutionTime = order.FilledAtUtc ?? DateTime.UtcNow;

                await writeSession.SaveChangesAsync(cancellationToken);

                updatedCount++;
                changedIds.Add(doc.Id);

                await BroadcastOrderUpdateAsync(doc, cancellationToken);
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(ex,
                    "Alpaca sync failed for trade {TradeId} (order {OrderId})",
                    trade.Id, trade.OrderId);
            }
        }

        // Second pass: import any Alpaca orders we don't know about.
        try
        {
            var afterUtc = DateTime.UtcNow - ExternalOrderLookback;
            var alpacaOrders = await _alpaca.ListOrdersAsync(
                OrderStatusFilter.All, afterUtc, ExternalOrderLimit, cancellationToken);

            foreach (var order in alpacaOrders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var orderIdStr = order.OrderId.ToString();
                if (knownOrderIds.Contains(orderIdStr)) continue;

                try
                {
                    var doc = new TradeDocument
                    {
                        Symbol = order.Symbol,
                        Action = order.OrderSide.ToString(),
                        RequestedQuantity = order.Quantity ?? order.FilledQuantity,
                        OrderId = orderIdStr,
                        FillStatus = MapOrderStatus(order.OrderStatus),
                        FilledQuantity = order.FilledQuantity,
                        AverageFillPrice = order.AverageFillPrice,
                        ExecutionTime = order.FilledAtUtc,
                        CreatedAt = order.CreatedAtUtc ?? DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        RiskAssessmentSummary = "(imported from Alpaca)",
                        PackId = ExternalPackId,
                        HoundId = ExternalHoundId,
                    };

                    using var writeSession = _store.OpenAsyncSession(TradeDatabase);
                    await writeSession.StoreAsync(doc, cancellationToken);
                    await writeSession.SaveChangesAsync(cancellationToken);

                    importedCount++;
                    changedIds.Add(doc.Id);
                    knownOrderIds.Add(orderIdStr);

                    await BroadcastOrderUpdateAsync(doc, cancellationToken);
                }
                catch (Exception ex)
                {
                    errors++;
                    _logger.LogWarning(ex,
                        "Failed to import external Alpaca order {OrderId} ({Symbol} {Side})",
                        orderIdStr, order.Symbol, order.OrderSide);
                }
            }
        }
        catch (Exception ex)
        {
            errors++;
            _logger.LogError(ex, "Failed to list external Alpaca orders");
        }

        var result = new AlpacaSyncResult(
            checkedCount, updatedCount, importedCount, errors,
            startedAt, DateTime.UtcNow,
            changedIds);

        if (updatedCount > 0 || importedCount > 0 || errors > 0)
        {
            _logger.LogInformation(
                "Alpaca sync complete: checked={Checked} updated={Updated} imported={Imported} errors={Errors}",
                checkedCount, updatedCount, importedCount, errors);
        }

        return result;
    }

    private Task BroadcastOrderUpdateAsync(TradeDocument doc, CancellationToken cancellationToken)
    {
        return _hub.Clients
            .Group("pack-trading-pack")
            .SendAsync("OnOrderUpdate", new
            {
                tradeDocumentId = doc.Id,
                symbol = doc.Symbol,
                fillStatus = doc.FillStatus.ToString(),
                filledQuantity = doc.FilledQuantity,
                averageFillPrice = doc.AverageFillPrice,
                executionTime = doc.ExecutionTime,
            }, cancellationToken);
    }

    private static FillStatus MapOrderStatus(OrderStatus status) => status switch
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
