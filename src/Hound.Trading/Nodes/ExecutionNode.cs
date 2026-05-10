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
/// Institutional-grade execution node. Places orders via Alpaca,
/// persists <see cref="TradeDocument"/> to RavenDB for lifecycle tracking,
/// and transitions the graph to the Monitor phase.
/// </summary>
public class ExecutionNode : INode
{
    public string NodeId => "execution-node";
    public string PackId => "trading-pack";
    private const string Database = "hound-trading-pack";

    private readonly ChatClientAgent _agent;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;
    private readonly IDocumentStore _documentStore;

    public ExecutionNode(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        IDocumentStore documentStore,
        ILoggerFactory? loggerFactory = null)
    {
        _alpacaService = alpacaService;
        _activityLogger = activityLogger;
        _documentStore = documentStore;

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Symbol")] string symbol,
                 [System.ComponentModel.Description("Number of shares")] decimal quantity,
                 [System.ComponentModel.Description("Buy or Sell")] string side) =>
                    PlaceMarketOrderAsync(symbol, quantity, side),
                "place_market_order",
                "Places a market order for the specified symbol and returns the order ID and status"),

            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Alpaca order ID (GUID)")] string orderId) =>
                    GetOrderStatusAsync(orderId),
                "get_order_status",
                "Retrieves the current status, filled quantity, and average fill price for an order"),

            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Alpaca order ID (GUID)")] string orderId) =>
                    CancelOrderAsync(orderId),
                "cancel_order",
                "Cancels an open order by its ID"),
        };

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are ExecutionNode, an institutional-grade execution trader.
                Your role is precision order placement and lifecycle management.
                When given an approved trade, use the place_market_order tool to submit the order.
                After placement, you may use get_order_status to check fill progress or cancel_order to abort.
                Respond strictly in JSON matching:
                {"success":true,"symbol":"...","action":"Buy|Sell","quantity":0.0,"filledPrice":null,"orderId":"...","message":"..."}
                """,
            name: "ExecutionNode",
            description: "Institutional-grade execution trader with order lifecycle management",
            tools: tools,
            loggerFactory: loggerFactory);
    }

    public async Task<TradingGraphState> ExecuteAsync(
        TradingGraphState state, CancellationToken cancellationToken)
    {
        var assessment = state.RiskOutput!;

        if (assessment.Verdict == RiskVerdict.Rejected)
        {
            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = NodeId,
                HoundName = "ExecutionNode",
                Message = $"Trade rejected by RiskNode: {assessment.Reasoning}",
                Severity = ActivitySeverity.Warning,
            }, cancellationToken);

            var failResult = new ExecutionResult(
                false,
                assessment.Decision.Symbol,
                assessment.Decision.Action,
                assessment.Decision.Quantity,
                null,
                string.Empty,
                $"Rejected: {assessment.Reasoning}");

            return state with { ExecutionOutput = failResult, IsComplete = true };
        }

        var effectiveQuantity = assessment.AdjustedQuantity ?? assessment.Decision.Quantity;

        // Create TradeDocument with Pending status before placing the order
        var tradeDoc = new TradeDocument
        {
            Symbol = assessment.Decision.Symbol,
            Action = assessment.Decision.Action.ToString(),
            RequestedQuantity = effectiveQuantity,
            FillStatus = FillStatus.Pending,
            RiskAssessmentSummary = assessment.Reasoning,
            PackId = PackId,
            HoundId = NodeId,
        };

        using (var session = _documentStore.OpenAsyncSession(Database))
        {
            await session.StoreAsync(tradeDoc, cancellationToken);
            await session.SaveChangesAsync(cancellationToken);
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "ExecutionNode",
            Message = $"Executing {assessment.Decision.Action} {effectiveQuantity} {assessment.Decision.Symbol}",
            Severity = ActivitySeverity.Info,
            Metadata = new Dictionary<string, object>
            {
                ["tradeDocumentId"] = tradeDoc.Id,
            },
        }, cancellationToken);

        var effectiveDecision = assessment.Decision with { Quantity = effectiveQuantity };
        var decisionJson = JsonSerializer.Serialize(effectiveDecision);

        var agentSession = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"Execute this approved trade:\n{decisionJson}")],
            agentSession,
            cancellationToken: cancellationToken);

        var json = LlmResponseParser.ExtractJson(response.Text ?? "{}");
        ExecutionResult result;

        try
        {
            var parsed = JsonSerializer.Deserialize<ExecutionResult>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
            result = parsed ?? new ExecutionResult(false, assessment.Decision.Symbol,
                assessment.Decision.Action, effectiveQuantity, null, string.Empty, "Execution failed");
        }
        catch (JsonException)
        {
            result = new ExecutionResult(false, assessment.Decision.Symbol,
                assessment.Decision.Action, effectiveQuantity, null, string.Empty, json,
                tradeDoc.Id);
        }

        // Update TradeDocument with OrderId from the placed order
        using (var session = _documentStore.OpenAsyncSession(Database))
        {
            var doc = await session.LoadAsync<TradeDocument>(tradeDoc.Id, cancellationToken);
            if (doc is not null)
            {
                doc.OrderId = result.OrderId ?? string.Empty;
                doc.UpdatedAt = DateTime.UtcNow;
                if (!result.Success)
                    doc.FillStatus = FillStatus.Rejected;
                await session.SaveChangesAsync(cancellationToken);
            }
        }

        result = result with { TradeDocumentId = tradeDoc.Id };

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "ExecutionNode",
            Message = result.Success
                ? $"Order placed: {result.OrderId} — {result.Message}"
                : $"Execution failed: {result.Message}",
            Severity = result.Success ? ActivitySeverity.Success : ActivitySeverity.Error,
            Metadata = new Dictionary<string, object>
            {
                ["tradeDocumentId"] = tradeDoc.Id,
                ["orderId"] = result.OrderId ?? string.Empty,
            },
        }, cancellationToken);

        return state with
        {
            ExecutionOutput = result,
            Phase = result.Success ? GraphPhase.Monitor : state.Phase,
            IsComplete = !result.Success,
        };
    }

    private async Task<string> PlaceMarketOrderAsync(string symbol, decimal quantity, string side)
    {
        var orderSide = string.Equals(side, "Sell", StringComparison.OrdinalIgnoreCase)
            ? OrderSide.Sell
            : OrderSide.Buy;

        var order = await _alpacaService.SubmitOrderAsync(
            symbol,
            OrderQuantity.Fractional(quantity),
            orderSide,
            OrderType.Market,
            TimeInForce.Day);

        return JsonSerializer.Serialize(new
        {
            orderId = order.OrderId.ToString(),
            status = order.OrderStatus.ToString(),
            symbol = order.Symbol,
            quantity = order.Quantity,
            side = order.OrderSide.ToString(),
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

    private async Task<string> CancelOrderAsync(string orderId)
    {
        if (!Guid.TryParse(orderId, out var guid))
            return JsonSerializer.Serialize(new { error = "Invalid order ID format", success = false });

        var success = await _alpacaService.CancelOrderAsync(guid);

        return JsonSerializer.Serialize(new { orderId, success });
    }
}
