using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hound.Trading.Nodes;

/// <summary>
/// Monitors open trades by checking order fill status and current positions via Alpaca.
/// Absorbs the responsibilities of the former <c>OrderWatcherService</c>.
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

    private readonly ChatClientAgent _agent;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;
    private readonly IDocumentStore _documentStore;
    private readonly IResettableExecutor _resetter;
    private readonly int _monitorDelaySeconds;
    private readonly ILogger<MonitorNode>? _logger;

    public MonitorNode(
        IChatClient chatClient,
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

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Stock ticker symbol")] string symbol) =>
                    CheckPositionAsync(symbol),
                "check_position",
                "Checks current position for a symbol including quantity and unrealized P&L"),

            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Alpaca order ID (GUID)")] string orderId) =>
                    GetOrderStatusAsync(orderId),
                "get_order_status",
                "Retrieves the current status, filled quantity, and average fill price for an order"),
        };

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are MonitorNode, a trade lifecycle monitor.
                Your role is to check the status of placed orders and open positions.
                Use get_order_status to check if the order has been filled.
                Use check_position to see current holdings and unrealized P&L.
                Determine if the trade is still open (position held) or closed (no position).
                Respond strictly in JSON matching:
                {"tradeOpen":true,"currentStatus":"Filled|Pending|PartiallyFilled|Canceled|Expired|Rejected","currentPrice":null,"unrealizedPnL":null,"summary":"..."}
                """,
            name: "MonitorNode",
            description: "Monitors trade lifecycle — order fills and position status",
            tools: tools,
            loggerFactory: loggerFactory);
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

        // Fetch authoritative facts from Alpaca BEFORE invoking the LLM so we
        // can both ground the model's narrative and overwrite any bookkeeping
        // fields it tries to confabulate. The LLM never gets to decide
        // tradeOpen or currentStatus — those come straight from the broker.
        FillStatus authoritativeStatus = FillStatus.Pending;
        decimal? currentPrice = null;
        decimal? unrealizedPnL = null;
        var tradeOpen = false;
        string? orderFacts = null;
        string? positionFacts = null;

        try
        {
            var order = await _alpacaService.GetOrderAsync(orderGuid, cancellationToken);
            authoritativeStatus = MapOrderStatus(order.OrderStatus);
            orderFacts = JsonSerializer.Serialize(new
            {
                orderId = order.OrderId.ToString(),
                status = order.OrderStatus.ToString(),
                filledQuantity = order.FilledQuantity,
                averageFillPrice = order.AverageFillPrice,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MonitorNode failed to fetch order {OrderId}", execution.OrderId);
            orderFacts = $"{{\"error\":\"order lookup failed: {ex.Message}\"}}";
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
                positionFacts = JsonSerializer.Serialize(new
                {
                    hasPosition = true,
                    quantity = position.Quantity,
                    marketValue = position.MarketValue,
                    unrealizedPnL = position.UnrealizedProfitLoss,
                    currentPrice = position.AssetCurrentPrice,
                    averageEntryPrice = position.AverageEntryPrice,
                });
            }
            else
            {
                positionFacts = JsonSerializer.Serialize(new { hasPosition = false, quantity = 0m });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "MonitorNode failed to fetch position for {Symbol}", state.Symbol);
            positionFacts = $"{{\"error\":\"position lookup failed: {ex.Message}\"}}";
        }

        // An order that is still pending (not yet filled, not rejected/canceled/expired)
        // means the trade lifecycle is still active even if no position exists yet
        // (e.g., order submitted pre-market, awaiting fill at open).
        if (!tradeOpen && authoritativeStatus is FillStatus.Pending or FillStatus.PartiallyFilled)
        {
            tradeOpen = true;
        }

        var prompt =
            $"Trade lifecycle check for {state.Symbol}.\n\n" +
            $"Authoritative facts (already fetched from Alpaca — do NOT call tools, just summarise):\n" +
            $"  order: {orderFacts}\n" +
            $"  position: {positionFacts}\n" +
            $"  derived tradeOpen: {tradeOpen}\n" +
            $"  derived currentStatus: {authoritativeStatus}\n\n" +
            "Produce the JSON response. The `tradeOpen` and `currentStatus` fields MUST mirror the derived values above exactly — they will be overwritten otherwise. Use `summary` to describe the situation in one or two sentences.";

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            session,
            cancellationToken: cancellationToken);

        var json = LlmResponseParser.ExtractJson(response.Text ?? "{}");
        string summary;
        try
        {
            var parsed = JsonSerializer.Deserialize<MonitorResult>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
            summary = parsed?.Summary ?? "No summary produced.";
        }
        catch (JsonException)
        {
            summary = json;
        }

        // Overwrite LLM-supplied bookkeeping with the authoritative values.
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
            // Non-fatal — the LLM agent will still attempt to check via tools
        }
    }

    private async Task<string> CheckPositionAsync(string symbol)
    {
        var positions = await _alpacaService.ListPositionsAsync();
        var position = positions.FirstOrDefault(p =>
            string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        if (position is null)
            return JsonSerializer.Serialize(new { symbol, hasPosition = false, quantity = 0m });

        return JsonSerializer.Serialize(new
        {
            symbol = position.Symbol,
            hasPosition = true,
            quantity = position.Quantity,
            marketValue = position.MarketValue,
            unrealizedPnL = position.UnrealizedProfitLoss,
            currentPrice = position.AssetCurrentPrice,
            averageEntryPrice = position.AverageEntryPrice,
        });
    }

    private async Task<string> GetOrderStatusAsync(string orderId)
    {
        if (!Guid.TryParse(orderId, out var guid))
            return JsonSerializer.Serialize(new { error = "Invalid order ID format" });

        var order = await _alpacaService.GetOrderAsync(guid);

        return JsonSerializer.Serialize(new
        {
            orderId = order.OrderId.ToString(),
            status = order.OrderStatus.ToString(),
            symbol = order.Symbol,
            filledQuantity = order.FilledQuantity,
            averageFillPrice = order.AverageFillPrice,
            submittedAt = order.SubmittedAtUtc,
            filledAt = order.FilledAtUtc,
        });
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
