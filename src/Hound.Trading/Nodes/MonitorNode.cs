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

        var prompt = $"Check the status of order {execution.OrderId} for {state.Symbol} " +
                     $"and the current position. Trade document: {execution.TradeDocumentId}";

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            session,
            cancellationToken: cancellationToken);

        var json = LlmResponseParser.ExtractJson(response.Text ?? "{}");
        MonitorResult monitor;

        try
        {
            var result = JsonSerializer.Deserialize<MonitorResult>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
            monitor = result ?? new MonitorResult(false, FillStatus.Pending, null, null, "Unable to parse monitor response");
        }
        catch (JsonException)
        {
            monitor = new MonitorResult(false, FillStatus.Pending, null, null, json);
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "MonitorNode",
            Message = monitor.TradeOpen
                ? $"Trade still open for {state.Symbol}: {monitor.Summary}"
                : $"Trade closed for {state.Symbol}: {monitor.Summary}",
            Severity = monitor.TradeOpen ? ActivitySeverity.Info : ActivitySeverity.Success,
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
