using System.Globalization;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Graph;
using Hound.Grocery.Services;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Nodes;

/// <summary>
/// Drives the <c>grocery-browser</c> sidecar to turn a <see cref="ShoppingPlan"/>
/// into a real Sainsbury's basket (spec §7.2, §10): for each planned item it
/// searches, ranks candidates (favourite → Nectar price → preferred/closest
/// match via <see cref="ProductRanker"/>), auto-substitutes the closest in-stock
/// match when the preferred product is unavailable (recording the reason), adds
/// it to the basket, then reads the basket back to emit a <see cref="BasketResult"/>.
/// It writes the audit trace (<c>basket-trace.md</c>) and appends purchase
/// observations (<c>purchase-history.md</c>) so LearnerHound can learn.
/// <para>
/// <b>Hard safety rule R1:</b> ShopperHound stops once the basket is built. It
/// never selects a delivery slot and never checks out — those controls are
/// denylisted in the sidecar (URL + testid denylist + action allowlist), and no
/// method on <see cref="IBrowserWorkerClient"/> can reach them.
/// </para>
/// </summary>
public class ShopperHound : INode
{
    public string NodeId => "shopper-hound";
    public string PackId => GroceryPack.PackId;

    private readonly IActivityLogger _activityLogger;
    private readonly IBrowserWorkerClient _browserWorker;
    private readonly ProductRanker _ranker;
    private readonly BasketTraceService _basketTrace;
    private readonly PurchaseHistoryService _purchaseHistory;
    private readonly ILogger<ShopperHound>? _logger;

    public ShopperHound(
        IActivityLogger activityLogger,
        IBrowserWorkerClient browserWorker,
        ProductRanker ranker,
        BasketTraceService basketTrace,
        PurchaseHistoryService purchaseHistory,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _browserWorker = browserWorker;
        _ranker = ranker;
        _basketTrace = basketTrace;
        _purchaseHistory = purchaseHistory;
        _logger = loggerFactory?.CreateLogger<ShopperHound>();
    }

    /// <summary>One chosen line of the build, retained for trace + learning.</summary>
    internal record ShopSelection(
        PlannedItem Item,
        ProductCandidate Chosen,
        bool IsSubstitution,
        string? SubstitutionReason,
        double Quantity);

    /// <summary>The outcome of a build: the basket plus the per-item selections.</summary>
    internal record ShopOutcome(BasketResult Basket, IReadOnlyList<ShopSelection> Selections);

    public async Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken)
    {
        var plan = state.Plan ?? new ShoppingPlan(Array.Empty<PlannedItem>());
        var outcome = await ShopAsync(plan, DateTimeOffset.UtcNow, state.RunId, cancellationToken);
        return state with { CurrentNode = NodeId, Phase = GroceryPhase.Shopping, Basket = outcome.Basket };
    }

    /// <summary>
    /// Deterministic build core (the <paramref name="at"/> timestamp is injected
    /// so tests avoid <see cref="DateTimeOffset.UtcNow"/> nondeterminism). Logs
    /// activities, writes the basket trace and appends purchase observations.
    /// </summary>
    internal async Task<ShopOutcome> ShopAsync(
        ShoppingPlan plan,
        DateTimeOffset at,
        string runId,
        CancellationToken cancellationToken)
    {
        _logger?.LogInformation("ShopperHound building basket for {Count} planned item(s).", plan.Items.Count);

        var login = await _browserWorker.LoginAsync(cancellationToken);
        if (!login.Authenticated)
        {
            await LogAsync(
                "ShopperHound could not authenticate the Sainsbury's session; basket may be incomplete.",
                ActivitySeverity.Warning,
                new Dictionary<string, object> { ["event"] = "ShopBlocked", ["reason"] = login.Message ?? "not authenticated" },
                cancellationToken);
        }

        var selections = new List<ShopSelection>();
        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var search = await _browserWorker.SearchAsync(item.SearchTerm, cancellationToken);
            var ranked = _ranker.Rank(item, search.Candidates);
            if (ranked.Chosen is null)
            {
                await LogAsync(
                    $"No in-stock candidate found for '{item.RawListText}' — skipped.",
                    ActivitySeverity.Warning,
                    new Dictionary<string, object> { ["event"] = "ItemUnavailable", ["item"] = item.RawListText },
                    cancellationToken);
                continue;
            }

            await _browserWorker.AddToBasketAsync(ranked.Chosen.ProductId, item.TargetQuantity, cancellationToken);
            selections.Add(new ShopSelection(item, ranked.Chosen, ranked.IsSubstitution, ranked.SubstitutionReason, item.TargetQuantity));
        }

        var snapshot = await _browserWorker.GetBasketAsync(cancellationToken);
        var basket = BuildBasket(snapshot, selections);

        await _basketTrace.AppendRunAsync(runId, basket, at, cancellationToken);
        await _purchaseHistory.AppendAsync(BuildObservations(selections, at), cancellationToken);

        var substitutions = selections.Count(s => s.IsSubstitution);
        await LogAsync(
            $"Basket built: {basket.Lines.Count} line(s), subtotal £{basket.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)}, {substitutions} substitution(s).",
            ActivitySeverity.Success,
            new Dictionary<string, object>
            {
                ["event"] = "BasketBuilt",
                ["lines"] = basket.Lines.Count,
                ["subtotal"] = basket.Subtotal,
                ["substitutions"] = substitutions,
            },
            cancellationToken);

        return new ShopOutcome(basket, selections);
    }

    /// <summary>
    /// Merges the sidecar's basket read-back with the chosen-candidate metadata
    /// (favourite flag + substitution reason aren't present on the gol-ui
    /// trolley). Falls back to synthesising lines from the selections when the
    /// sidecar returns an empty basket (e.g. the DRY_RUN kill switch).
    /// </summary>
    private static BasketResult BuildBasket(BasketSnapshot snapshot, IReadOnlyList<ShopSelection> selections)
    {
        var byId = selections
            .GroupBy(s => s.Chosen.ProductId)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        if (snapshot.Lines.Count > 0)
        {
            var enriched = snapshot.Lines.Select(line =>
            {
                if (byId.TryGetValue(line.ProductId, out var sel))
                {
                    return line with
                    {
                        IsFavourite = line.IsFavourite || sel.Chosen.IsFavourite,
                        SubstitutionReason = line.SubstitutionReason ?? sel.SubstitutionReason,
                    };
                }

                return line;
            }).ToList();

            return new BasketResult(enriched, snapshot.Subtotal);
        }

        // No basket read-back (dry-run / mock): synthesise from selections.
        var lines = selections.Select(s => new BasketLine(
            s.Chosen.ProductId,
            s.Chosen.Name,
            s.Quantity,
            s.Chosen.EffectivePrice,
            s.Chosen.IsNectarPrice,
            s.Chosen.IsFavourite,
            s.SubstitutionReason)).ToList();
        var subtotal = lines.Aggregate(0m, (sum, l) => sum + (l.Price * (decimal)l.Quantity));
        return new BasketResult(lines, subtotal);
    }

    private static IReadOnlyList<PurchaseObservation> BuildObservations(
        IReadOnlyList<ShopSelection> selections, DateTimeOffset at)
    {
        var date = DateOnly.FromDateTime(at.UtcDateTime);
        return selections
            .Select(s => new PurchaseObservation(s.Item.RawListText, s.Chosen.Name, s.Quantity, date))
            .ToList();
    }

    private Task LogAsync(string message, ActivitySeverity severity, Dictionary<string, object> metadata, CancellationToken ct) =>
        _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(ShopperHound),
            Message = message,
            Severity = severity,
            Metadata = metadata,
        }, ct);
}
