using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents;
using System.Text.Json;

namespace Hound.Trading.Nodes;

/// <summary>
/// Monitors open trades by checking order fill status and current positions via Alpaca.
/// Purely deterministic — no LLM call; all data comes directly from the broker API.
/// <para>
/// When <see cref="MonitorResult.TradeOpen"/> is <c>true</c>, the graph loops back
/// to <see cref="AnalystsTeamNode"/> after a configured delay and KV cache reset.
/// </para>
/// </summary>
public class MonitorNode : INode
{
    public string NodeId => "monitor-node";
    public string PackId => "trading-pack";
    private const string Database = "hound-trading-pack";

    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;
    private readonly IDocumentStore _documentStore;
    private readonly IResettableExecutor _resetter;
    private readonly int _monitorDelaySeconds;
    private readonly ILogger<MonitorNode>? _logger;

    public MonitorNode(
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        IDocumentStore documentStore,
        IResettableExecutor resetter,
        int monitorDelaySeconds = 60,
        ILoggerFactory? loggerFactory = null)
    {
        _alpacaService = alpacaService;
        _activityLogger = activityLogger;
        _documentStore = documentStore;
        _resetter = resetter;
        _monitorDelaySeconds = monitorDelaySeconds;
        _logger = loggerFactory?.CreateLogger<MonitorNode>();
    }

    public async Task<TradingGraphState> ExecuteAsync(
        TradingGraphState state, CancellationToken cancellationToken)
    {
        var execution = state.ExecutionOutput!;

        // Short-circuit: if Execution never produced a valid order ID there is
        // nothing to monitor. Log an Error (not a Success) and end the run so
        // a hallucinated "trade closed" cannot leak into the dashboard.
        if (string.IsNullOrWhiteSpace(execution.OrderId)
            || !Guid.TryParse(execution.OrderId, out var orderGuid)
            || orderGuid == Guid.Empty)
        {
            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = NodeId,
                HoundName = "MonitorNode",
                Message = $"No valid order ID for {state.Symbol} — nothing to monitor. Treating run as failed.",
                Severity = ActivitySeverity.Error,
                Metadata = new Dictionary<string, object>
                {
                    ["orderId"] = execution.OrderId ?? string.Empty,
                    ["monitorCycle"] = state.MonitorCycleCount,
                },
            }, cancellationToken);

            var noOrder = new MonitorResult(
                TradeOpen: false,
                CurrentStatus: FillStatus.Rejected,
                CurrentPrice: null,
                UnrealizedPnL: null,
                Summary: "No order was placed; nothing to monitor.");

            return state with
            {
                MonitorOutput = noOrder,
                MonitorCycleCount = state.MonitorCycleCount + 1,
                IsComplete = true,
            };
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "MonitorNode",
            Message = $"Monitoring trade for {state.Symbol} (order: {execution.OrderId}, cycle #{state.MonitorCycleCount})",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        // Update TradeDocument status from Alpaca
        await UpdateTradeDocumentAsync(execution, cancellationToken);

        // Fetch authoritative facts from Alpaca.
        FillStatus authoritativeStatus = FillStatus.Pending;
        decimal? currentPrice = null;
        decimal? unrealizedPnL = null;
        var tradeOpen = false;

        try
        {
            var order = await _alpacaService.GetOrderAsync(orderGuid, cancellationToken);
            authoritativeStatus = MapOrderStatus(order.OrderStatus);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MonitorNode failed to fetch order {OrderId}", execution.OrderId);
        }

        try
        {
            var positions = await _alpacaService.ListPositionsAsync(cancellationToken);
            var position = positions.FirstOrDefault(p =>
                string.Equals(p.Symbol, state.Symbol, StringComparison.OrdinalIgnoreCase));
            if (position is not null && position.Quantity != 0)
            {
                tradeOpen = true;
                currentPrice = position.AssetCurrentPrice;
                unrealizedPnL = position.UnrealizedProfitLoss;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MonitorNode failed to fetch position for {Symbol}", state.Symbol);
        }

        // An order that is still pending (not yet filled, not rejected/canceled/expired)
        // means the trade lifecycle is still active even if no position exists yet
        // (e.g., order submitted pre-market, awaiting fill at open).
        if (!tradeOpen && authoritativeStatus is FillStatus.Pending or FillStatus.PartiallyFilled)
        {
            tradeOpen = true;
        }

        // Build deterministic summary from broker data.
        var summary = BuildSummary(state.Symbol, authoritativeStatus, tradeOpen, currentPrice, unrealizedPnL);

        var monitor = new MonitorResult(
            TradeOpen: tradeOpen,
            CurrentStatus: authoritativeStatus,
            CurrentPrice: currentPrice,
            UnrealizedPnL: unrealizedPnL,
            Summary: summary);

        // Severity policy: Success only when the order filled AND the position
        // has been fully closed (a real round-trip). Anything else is Info or
        // worse so the dashboard never glosses over a non-trade.
        ActivitySeverity severity;
        string message;
        if (authoritativeStatus is FillStatus.Rejected or FillStatus.Canceled or FillStatus.Expired)
        {
            severity = ActivitySeverity.Error;
            message = $"Order {authoritativeStatus} for {state.Symbol}: {summary}";
        }
        else if (tradeOpen)
        {
            severity = ActivitySeverity.Info;
            message = $"Trade still open for {state.Symbol}: {summary}";
        }
        else if (authoritativeStatus == FillStatus.Filled)
        {
            severity = ActivitySeverity.Success;
            message = $"Trade closed for {state.Symbol}: {summary}";
        }
        else
        {
            severity = ActivitySeverity.Info;
            message = $"Trade pending for {state.Symbol} (status: {authoritativeStatus}): {summary}";
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "MonitorNode",
            Message = message,
            Severity = severity,
            Metadata = new Dictionary<string, object>
            {
                ["tradeOpen"] = monitor.TradeOpen,
                ["currentStatus"] = monitor.CurrentStatus.ToString(),
                ["monitorCycle"] = state.MonitorCycleCount,
            },
        }, cancellationToken);

        var newState = state with
        {
            MonitorOutput = monitor,
            MonitorCycleCount = state.MonitorCycleCount + 1,
        };

        // If trade is still open, delay and reset KV caches before looping
        if (monitor.TradeOpen)
        {
            await Task.Delay(TimeSpan.FromSeconds(_monitorDelaySeconds), cancellationToken);
            await _resetter.ResetAsync(cancellationToken);
        }

        return newState;
    }

    private async Task UpdateTradeDocumentAsync(
        ExecutionResult execution, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(execution.OrderId) || string.IsNullOrEmpty(execution.TradeDocumentId))
            return;

        if (!Guid.TryParse(execution.OrderId, out var orderId))
            return;

        try
        {
            var order = await _alpacaService.GetOrderAsync(orderId, cancellationToken);
            var newStatus = MapOrderStatus(order.OrderStatus);

            using var session = _documentStore.OpenAsyncSession(Database);
            var doc = await session.LoadAsync<TradeDocument>(execution.TradeDocumentId, cancellationToken);
            if (doc is null) return;

            doc.FillStatus = newStatus;
            doc.FilledQuantity = order.FilledQuantity;
            doc.AverageFillPrice = order.AverageFillPrice;
            doc.UpdatedAt = DateTime.UtcNow;

            if (newStatus == FillStatus.Filled)
                doc.ExecutionTime = DateTime.UtcNow;

            await session.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Non-fatal — the trade document update is best-effort
        }
    }

    private static string BuildSummary(
        string symbol, FillStatus status, bool tradeOpen, decimal? currentPrice, decimal? unrealizedPnL)
    {
        if (status is FillStatus.Rejected or FillStatus.Canceled or FillStatus.Expired)
            return $"Order for {symbol} was {status.ToString().ToLowerInvariant()}.";

        if (!tradeOpen && status == FillStatus.Filled)
            return $"Position for {symbol} has been closed.";

        if (tradeOpen && status == FillStatus.Filled)
        {
            var priceStr = currentPrice.HasValue ? $" at ${currentPrice.Value:F2}" : "";
            var pnlStr = unrealizedPnL.HasValue
                ? $" with unrealized P&L of {(unrealizedPnL.Value >= 0 ? "+" : "")}{unrealizedPnL.Value:F2}"
                : "";
            return $"Position open for {symbol}{priceStr}{pnlStr}.";
        }

        if (status is FillStatus.Pending or FillStatus.PartiallyFilled)
            return $"Order for {symbol} is {status.ToString().ToLowerInvariant()}, awaiting fill.";

        return $"Monitoring {symbol} — status: {status}.";
    }

    internal static FillStatus MapOrderStatus(OrderStatus orderStatus) => orderStatus switch
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
