using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Graph;
using Hound.Grocery.Services;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Nodes;

/// <summary>
/// Drives the <c>grocery-browser</c> sidecar: login (session reuse), search,
/// candidate evaluation (price, Nectar, favourite, stock), add-to-basket and
/// auto-substitution. Vision-assisted grounding via the <c>vision</c> client.
/// <para>
/// <b>Phase 1 scaffold:</b> returns an empty basket placeholder. It does
/// <b>not</b> call the browser worker and makes no LLM calls yet (Phase 4). The
/// hard safety rule (never select a slot, never check out) is enforced in the
/// sidecar's URL denylist + action allowlist (spec §11.4).
/// </para>
/// </summary>
public class ShopperHound : INode
{
    public string NodeId => "shopper-hound";
    public string PackId => GroceryPack.PackId;

    private readonly IActivityLogger _activityLogger;
    private readonly IBrowserWorkerClient _browserWorker;
    private readonly ILogger<ShopperHound>? _logger;

    public ShopperHound(
        IActivityLogger activityLogger,
        IBrowserWorkerClient browserWorker,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _browserWorker = browserWorker;
        _logger = loggerFactory?.CreateLogger<ShopperHound>();
    }

    public async Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("ShopperHound stub invoked for run {RunId}", state.RunId);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(ShopperHound),
            Message = "ShopperHound scaffold placeholder (Phase 1 — no browser/LLM calls yet)",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var basket = new BasketResult(Array.Empty<BasketLine>(), 0m);
        return state with { CurrentNode = NodeId, Phase = GroceryPhase.Shopping, Basket = basket };
    }
}
