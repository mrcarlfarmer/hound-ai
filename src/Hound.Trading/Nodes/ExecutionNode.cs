using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents;

namespace Hound.Trading.Nodes;

/// <summary>
/// Institutional-grade execution node. Places orders via Alpaca,
/// persists <see cref="TradeDocument"/> to RavenDB for lifecycle tracking,
/// and transitions the graph to the Monitor phase.
/// <para>
/// Execution is fully deterministic — there is no LLM involved. The broker
/// response is the single source of truth for <see cref="ExecutionResult.Success"/>
/// and <see cref="ExecutionResult.OrderId"/>; nothing downstream can be misled
/// by a confabulated success.
/// </para>
/// </summary>
public class ExecutionNode : INode
{
    public string NodeId => "execution-node";
    public string PackId => "trading-pack";
    private const string Database = "hound-trading-pack";

    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;
    private readonly IDocumentStore _documentStore;
    private readonly ILogger<ExecutionNode>? _logger;

    public ExecutionNode(
        IAlpacaService alpacaService,
        IActivityLogger activityLogger,
        IDocumentStore documentStore,
        ILoggerFactory? loggerFactory = null)
    {
        _alpacaService = alpacaService;
        _activityLogger = activityLogger;
        _documentStore = documentStore;
        _logger = loggerFactory?.CreateLogger<ExecutionNode>();
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
        var symbol = assessment.Decision.Symbol;
        var action = assessment.Decision.Action;

        // Fractional-share guard: if the requested quantity is non-integer we
        // must confirm the symbol is fractionable on Alpaca. Whole-share orders
        // pass through unchanged. Failures are logged loudly so Tuner can learn
        // to avoid non-fractionable symbols when the account can only afford a
        // partial share.
        var isFractional = effectiveQuantity != Math.Truncate(effectiveQuantity);
        if (isFractional)
        {
            IAsset? asset = null;
            try
            {
                asset = await _alpacaService.GetAssetAsync(symbol, cancellationToken);
            }
            catch (Exception ex)
            {
                await _activityLogger.LogActivityAsync(new ActivityLog
                {
                    PackId = PackId,
                    HoundId = NodeId,
                    HoundName = "ExecutionNode",
                    Message = $"Asset lookup failed for {symbol}: {ex.Message}. Cannot verify fractionable support.",
                    Severity = ActivitySeverity.Error,
                }, cancellationToken);
            }

            if (asset is { Fractionable: false })
            {
                await _activityLogger.LogActivityAsync(new ActivityLog
                {
                    PackId = PackId,
                    HoundId = NodeId,
                    HoundName = "ExecutionNode",
                    Message = $"Rejected fractional order: {symbol} is not fractionable on Alpaca (requested quantity {effectiveQuantity}). Whole-share orders only for this symbol.",
                    Severity = ActivitySeverity.Error,
                }, cancellationToken);

                var rejectResult = new ExecutionResult(
                    false,
                    symbol,
                    action,
                    effectiveQuantity,
                    null,
                    string.Empty,
                    $"Symbol {symbol} does not support fractional shares on Alpaca; requested quantity {effectiveQuantity} requires a whole-share order.");

                return state with { ExecutionOutput = rejectResult, IsComplete = true };
            }
        }

        // Create TradeDocument with Pending status before placing the order
        var tradeDoc = new TradeDocument
        {
            Symbol = symbol,
            Action = action.ToString(),
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
            Message = $"Executing {action} {effectiveQuantity} {symbol}",
            Severity = ActivitySeverity.Info,
            Metadata = new Dictionary<string, object>
            {
                ["tradeDocumentId"] = tradeDoc.Id,
            },
        }, cancellationToken);

        // Deterministic order placement. The broker response is the only thing
        // that can set Success = true — no LLM, no self-reported success.
        //
        // Buys are placed as a market entry plus a follow-up trailing-stop
        // Sell (GTC) using the strategy-chosen TrailPercent so the position
        // has a protective exit that ratchets up with price (and never down).
        // Sells go out as plain market Day orders.
        ExecutionResult result;
        try
        {
            var orderSide = action == TradeAction.Sell ? OrderSide.Sell : OrderSide.Buy;
            var order = await _alpacaService.SubmitOrderAsync(
                symbol,
                OrderQuantity.Fractional(effectiveQuantity),
                orderSide,
                OrderType.Market,
                TimeInForce.Day,
                cancellationToken: cancellationToken);

            var orderIdGuid = order.OrderId;
            var success = orderIdGuid != Guid.Empty && order.OrderStatus != OrderStatus.Rejected;
            var orderIdString = orderIdGuid == Guid.Empty ? string.Empty : orderIdGuid.ToString();

            result = new ExecutionResult(
                success,
                symbol,
                action,
                effectiveQuantity,
                order.AverageFillPrice,
                orderIdString,
                success
                    ? $"Order submitted to Alpaca — status: {order.OrderStatus}"
                    : $"Alpaca returned non-actionable order status: {order.OrderStatus}",
                tradeDoc.Id);

            // Attach a trailing-stop Sell as the protective exit for any
            // accepted Buy. Submitted as a separate GTC order because Alpaca's
            // OTO/Bracket order classes don't support a trailing-stop child
            // leg; the broker parks the stop until the buy fills and the
            // position exists. Failures here are logged but do NOT mark the
            // Buy as failed — the position is open and MonitorNode can place
            // a stop on the next cycle if needed.
            if (success && action == TradeAction.Buy)
            {
                var trailPercent = assessment.Decision.TrailPercent
                    ?? StrategyNode.DefaultBuyTrailPercent;

                try
                {
                    var stopOrder = await _alpacaService.SubmitTrailingStopOrderAsync(
                        symbol,
                        OrderQuantity.Fractional(effectiveQuantity),
                        OrderSide.Sell,
                        trailPercent,
                        TimeInForce.Gtc,
                        cancellationToken: cancellationToken);

                    await _activityLogger.LogActivityAsync(new ActivityLog
                    {
                        PackId = PackId,
                        HoundId = NodeId,
                        HoundName = "ExecutionNode",
                        Message = $"Attached trailing-stop SELL exit for {symbol} @ {trailPercent}% — order {stopOrder.OrderId} status: {stopOrder.OrderStatus}",
                        Severity = ActivitySeverity.Info,
                        Metadata = new Dictionary<string, object>
                        {
                            ["tradeDocumentId"] = tradeDoc.Id,
                            ["entryOrderId"] = orderIdString,
                            ["stopOrderId"] = stopOrder.OrderId.ToString(),
                            ["trailPercent"] = trailPercent,
                        },
                    }, cancellationToken);
                }
                catch (Exception stopEx)
                {
                    _logger?.LogWarning(stopEx, "ExecutionNode failed to attach trailing-stop exit for {Symbol}", symbol);

                    await _activityLogger.LogActivityAsync(new ActivityLog
                    {
                        PackId = PackId,
                        HoundId = NodeId,
                        HoundName = "ExecutionNode",
                        Message = $"Buy entry for {symbol} succeeded but trailing-stop exit submission failed: {stopEx.Message}. Position is open without a protective stop.",
                        Severity = ActivitySeverity.Warning,
                        Metadata = new Dictionary<string, object>
                        {
                            ["tradeDocumentId"] = tradeDoc.Id,
                            ["entryOrderId"] = orderIdString,
                        },
                    }, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ExecutionNode order submission failed for {Symbol}", symbol);
            result = new ExecutionResult(
                false,
                symbol,
                action,
                effectiveQuantity,
                null,
                string.Empty,
                $"Order placement failed: {ex.Message}",
                tradeDoc.Id);
        }

        // Persist authoritative outcome to the TradeDocument
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
            IsComplete = !result.Success,
        };
    }
}
