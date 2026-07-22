using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;

namespace Hound.Trading.Services;

/// <summary>
/// Configuration for the <see cref="SoftwareStopPoller"/>.
/// </summary>
public sealed class SoftwareStopSettings
{
    public const string SectionName = "SoftwareStop";

    /// <summary>Seconds between software-stop evaluation cycles.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>
    /// When <c>true</c>, the poller evaluates stops even while the market is
    /// closed. Off by default — regular trailing stops only make sense during
    /// the regular session. Enable alongside <c>ExtendedHours</c> order support.
    /// </summary>
    public bool RunWhenMarketClosed { get; set; }
}

/// <summary>
/// Background service that emulates a trailing stop for FRACTIONAL positions,
/// which Alpaca won't protect with a broker-side stop order. Runs entirely on
/// direct broker API calls — no LLM/MCP involvement, keeping model overhead at
/// zero.
/// <para>
/// Each cycle (default every 60s, regular session only) it loads every
/// <see cref="TradeDocument"/> with <see cref="StopMode.SoftwareTrailing"/>
/// that hasn't yet triggered, advances each position's high-water mark, and
/// submits a market Sell when the latest trade price falls to or below the
/// trailing stop price.
/// </para>
/// <para>
/// This runs independently of the trading graph lifecycle: a graph run
/// completes once its monitor loop sees the position close, but a fractional
/// position can outlive many runs, so the protective stop must keep firing
/// from a long-lived background service rather than a graph node.
/// </para>
/// </summary>
public sealed class SoftwareStopPoller : BackgroundService
{
    private const string Database = "hound-trading-pack";
    private const string PackId = "trading-pack";
    private const string HoundName = "SoftwareStopPoller";

    private readonly IAlpacaService _alpacaService;
    private readonly IDocumentStore _documentStore;
    private readonly IActivityLogger _activityLogger;
    private readonly SoftwareStopSettings _settings;
    private readonly ILogger<SoftwareStopPoller>? _logger;

    public SoftwareStopPoller(
        IAlpacaService alpacaService,
        IDocumentStore documentStore,
        IActivityLogger activityLogger,
        IOptions<SoftwareStopSettings> settings,
        ILoggerFactory? loggerFactory = null)
    {
        _alpacaService = alpacaService;
        _documentStore = documentStore;
        _activityLogger = activityLogger;
        _settings = settings.Value;
        _logger = loggerFactory?.CreateLogger<SoftwareStopPoller>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.IntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a single bad cycle kill the poller — protective
                // stops must keep running for the life of the process.
                _logger?.LogWarning(ex, "SoftwareStopPoller cycle failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Evaluates one polling cycle. Exposed as <c>internal</c> so tests can
    /// drive a single deterministic cycle without spinning up the hosted-service
    /// loop.
    /// </summary>
    internal async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        // Gate on market hours unless explicitly told to run 24/7.
        if (!_settings.RunWhenMarketClosed)
        {
            var clock = await _alpacaService.GetClockAsync(cancellationToken);
            if (!clock.IsOpen)
                return;
        }

        List<TradeDocument> candidates;
        using (var session = _documentStore.OpenAsyncSession(Database))
        {
            candidates = await session.Query<TradeDocument>()
                .Where(d => d.StopMode == StopMode.SoftwareTrailing
                            && d.FillStatus == FillStatus.Filled
                            && d.StopTriggeredAt == null)
                .ToListAsync(cancellationToken);
        }

        if (candidates.Count == 0)
            return;

        // One position snapshot per cycle to detect externally-closed positions.
        IReadOnlyList<IPosition> positions;
        try
        {
            positions = await _alpacaService.ListPositionsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SoftwareStopPoller could not list positions; skipping cycle");
            return;
        }

        var openBySymbol = positions
            .Where(p => p.Quantity != 0)
            .GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Cache latest trades per unique symbol so multiple docs on the same
        // ticker only cost one data-API call.
        var priceCache = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Position closed elsewhere (manual sale, broker stop, etc.) — the
            // software stop is moot. Mark it resolved so we stop polling it.
            if (!openBySymbol.ContainsKey(doc.Symbol))
            {
                await ResolveClosedPositionAsync(doc.Id, cancellationToken);
                continue;
            }

            if (!priceCache.TryGetValue(doc.Symbol, out var price))
            {
                var trade = await _alpacaService.GetLatestTradeAsync(doc.Symbol, cancellationToken);
                price = trade?.Price;
                priceCache[doc.Symbol] = price;
            }

            if (price is not decimal lastPrice || lastPrice <= 0m)
                continue; // No usable price this cycle.

            await EvaluateStopAsync(doc.Id, lastPrice, cancellationToken);
        }
    }

    /// <summary>
    /// Loads the document fresh, advances the high-water mark, and either
    /// updates the trailing stop or fires a closing market Sell.
    /// </summary>
    private async Task EvaluateStopAsync(string docId, decimal lastPrice, CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(Database);
        var doc = await session.LoadAsync<TradeDocument>(docId, cancellationToken);
        if (doc is null || doc.StopTriggeredAt is not null || doc.StopMode != StopMode.SoftwareTrailing)
            return;

        var trailPercent = doc.TrailPercent ?? StrategyNode.DefaultBuyTrailPercent;
        var evaluation = ComputeStopUpdate(doc, lastPrice);

        if (evaluation.Triggered)
        {
            // Trigger: close the position at market. The quantity is the
            // filled fractional amount the entry produced.
            var quantity = doc.FilledQuantity > 0m ? doc.FilledQuantity : doc.RequestedQuantity;
            string? exitOrderId = null;
            try
            {
                var sell = await _alpacaService.SubmitOrderAsync(
                    doc.Symbol,
                    OrderQuantity.Fractional(quantity),
                    OrderSide.Sell,
                    OrderType.Market,
                    TimeInForce.Day,
                    cancellationToken: cancellationToken);
                exitOrderId = sell.OrderId == Guid.Empty ? null : sell.OrderId.ToString();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "SoftwareStopPoller failed to submit closing Sell for {Symbol}", doc.Symbol);
                await _activityLogger.LogActivityAsync(new ActivityLog
                {
                    PackId = PackId,
                    HoundId = "execution-node",
                    HoundName = HoundName,
                    Message = $"Software stop for {doc.Symbol} triggered at ${lastPrice:F2} (stop ${evaluation.StopPrice:F2}) but the closing Sell failed: {ex.Message}.",
                    Severity = ActivitySeverity.Error,
                    Metadata = new Dictionary<string, object>
                    {
                        ["tradeDocumentId"] = doc.Id,
                        ["lastPrice"] = lastPrice,
                        ["stopPrice"] = evaluation.StopPrice,
                    },
                }, cancellationToken);
                // Leave StopTriggeredAt unset so we retry next cycle.
                return;
            }

            doc.HighWaterMark = evaluation.HighWaterMark;
            doc.StopPrice = evaluation.StopPrice;
            doc.StopTriggeredAt = DateTime.UtcNow;
            doc.StopExitOrderId = exitOrderId;
            doc.UpdatedAt = DateTime.UtcNow;
            await session.SaveChangesAsync(cancellationToken);

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = "execution-node",
                HoundName = HoundName,
                Message = $"Software trailing stop fired for {doc.Symbol}: price ${lastPrice:F2} ≤ stop ${evaluation.StopPrice:F2} (high-water ${evaluation.HighWaterMark:F2}, trail {trailPercent}%). Submitted market Sell {exitOrderId} for {quantity} share(s).",
                Severity = ActivitySeverity.Warning,
                Metadata = new Dictionary<string, object>
                {
                    ["tradeDocumentId"] = doc.Id,
                    ["lastPrice"] = lastPrice,
                    ["stopPrice"] = evaluation.StopPrice,
                    ["highWaterMark"] = evaluation.HighWaterMark,
                    ["trailPercent"] = trailPercent,
                    ["exitOrderId"] = exitOrderId ?? string.Empty,
                },
            }, cancellationToken);
            return;
        }

        // No trigger — persist any high-water-mark advance so the stop ratchets
        // up. Only write when something actually changed to avoid churn.
        var hwmChanged = doc.HighWaterMark != evaluation.HighWaterMark;
        var stopChanged = doc.StopPrice != evaluation.StopPrice;
        if (hwmChanged || stopChanged)
        {
            doc.HighWaterMark = evaluation.HighWaterMark;
            doc.StopPrice = evaluation.StopPrice;
            doc.UpdatedAt = DateTime.UtcNow;
            await session.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Outcome of a single software-stop evaluation.
    /// </summary>
    internal readonly record struct StopEvaluation(decimal HighWaterMark, decimal StopPrice, bool Triggered);

    /// <summary>
    /// Pure trailing-stop math: advances the high-water mark, derives the
    /// trailing stop price, and decides whether the latest price has breached
    /// it. Lazily initialises the high-water mark from the entry price (or the
    /// current price when entry is still unknown). Has no I/O so it's the unit
    /// under test for the stop behaviour.
    /// </summary>
    internal static StopEvaluation ComputeStopUpdate(TradeDocument doc, decimal lastPrice)
    {
        var trailPercent = doc.TrailPercent ?? StrategyNode.DefaultBuyTrailPercent;

        // Lazily initialise the high-water mark when the entry fill price was
        // unknown at execution time (market order still PendingNew). The first
        // observed trade after fill is a close-enough baseline.
        var highWaterMark = doc.HighWaterMark ?? doc.EntryPrice ?? lastPrice;
        if (lastPrice > highWaterMark)
            highWaterMark = lastPrice;

        var stopPrice = highWaterMark * (1m - trailPercent / 100m);
        var triggered = lastPrice <= stopPrice;

        return new StopEvaluation(highWaterMark, stopPrice, triggered);
    }

    /// <summary>
    /// Marks a software-stop document as resolved when its position has already
    /// been closed outside the poller, so we stop polling it.
    /// </summary>
    private async Task ResolveClosedPositionAsync(string docId, CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(Database);
        var doc = await session.LoadAsync<TradeDocument>(docId, cancellationToken);
        if (doc is null || doc.StopTriggeredAt is not null)
            return;

        // Use StopTriggeredAt as the "no longer actively monitored" marker, but
        // leave StopExitOrderId null to signal the close happened elsewhere.
        doc.StopTriggeredAt = DateTime.UtcNow;
        doc.UpdatedAt = DateTime.UtcNow;
        await session.SaveChangesAsync(cancellationToken);
    }
}
