using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent: Executes approved trades via Alpaca API. Logs results to activity logger.
/// </summary>
public class ExecutionHound
{
    private const string HoundId = "execution-hound";
    private const string PackId = "trading-pack";

    private readonly ChatClientAgent _agent;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;

    public ExecutionHound(
        IChatClient chatClient,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
    {
        _alpacaService = alpacaService;
        _activityLogger = activityLogger;

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                ([System.ComponentModel.Description("Symbol")] string symbol,
                 [System.ComponentModel.Description("Number of shares")] decimal quantity,
                 [System.ComponentModel.Description("Buy or Sell")] string side) =>
                    PlaceMarketOrderAsync(symbol, quantity, side),
                "place_market_order",
                "Places a market order for the specified symbol"),
        };

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are ExecutionHound, a trade execution specialist.
                When given an approved trade, use the place_market_order tool to execute it.
                Respond strictly in JSON matching:
                {"success":true,"symbol":"...","action":"Buy|Sell","quantity":0.0,"filledPrice":null,"orderId":"...","message":"..."}
                """,
            name: "ExecutionHound",
            description: "Executes approved trades via Alpaca API",
            tools: tools,
            loggerFactory: loggerFactory);
    }

    public async Task<ExecutionResult> ExecuteAsync(
        RiskAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        if (assessment.Verdict == RiskVerdict.Rejected)
        {
            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = HoundId,
                HoundName = "ExecutionHound",
                Message = $"Trade rejected by RiskHound: {assessment.Reasoning}",
                Severity = ActivitySeverity.Warning,
            }, cancellationToken);

            return new ExecutionResult(
                false,
                assessment.Decision.Symbol,
                assessment.Decision.Action,
                assessment.Decision.Quantity,
                null,
                string.Empty,
                $"Rejected: {assessment.Reasoning}");
        }

        var effectiveQuantity = assessment.AdjustedQuantity ?? assessment.Decision.Quantity;

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = HoundId,
            HoundName = "ExecutionHound",
            Message = $"Executing {assessment.Decision.Action} {effectiveQuantity} {assessment.Decision.Symbol}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var effectiveDecision = assessment.Decision with { Quantity = effectiveQuantity };
        var decisionJson = JsonSerializer.Serialize(effectiveDecision);

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"Execute this approved trade:\n{decisionJson}")],
            session,
            cancellationToken: cancellationToken);

        var json = response.Text ?? "{}";

        try
        {
            var result = JsonSerializer.Deserialize<ExecutionResult>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            result ??= new ExecutionResult(false, assessment.Decision.Symbol,
                assessment.Decision.Action, effectiveQuantity, null, string.Empty, "Execution failed");

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = HoundId,
                HoundName = "ExecutionHound",
                Message = result.Success
                    ? $"Order placed: {result.OrderId} — {result.Message}"
                    : $"Execution failed: {result.Message}",
                Severity = result.Success ? ActivitySeverity.Success : ActivitySeverity.Error,
            }, cancellationToken);

            return result;
        }
        catch (JsonException)
        {
            return new ExecutionResult(false, assessment.Decision.Symbol,
                assessment.Decision.Action, effectiveQuantity, null, string.Empty, json);
        }
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
}
