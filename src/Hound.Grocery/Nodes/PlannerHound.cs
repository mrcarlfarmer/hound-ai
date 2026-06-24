using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Graph;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Nodes;

/// <summary>
/// Turns the loose shopping list into a concrete <see cref="ShoppingPlan"/>
/// (search terms, candidate products, target quantities) using learned
/// preferences, favourites and budget guidance.
/// <para>
/// <b>Phase 1 scaffold:</b> returns an empty plan placeholder. No LLM calls are
/// wired up yet (Phase 4).
/// </para>
/// </summary>
public class PlannerHound : INode
{
    public string NodeId => "planner-hound";
    public string PackId => GroceryPack.PackId;

    private readonly IActivityLogger _activityLogger;
    private readonly ILogger<PlannerHound>? _logger;

    public PlannerHound(IActivityLogger activityLogger, ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _logger = loggerFactory?.CreateLogger<PlannerHound>();
    }

    public async Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("PlannerHound stub invoked for run {RunId}", state.RunId);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(PlannerHound),
            Message = "PlannerHound scaffold placeholder (Phase 1 — no behaviour yet)",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var plan = new ShoppingPlan(Array.Empty<PlannedItem>());
        return state with { CurrentNode = NodeId, Phase = GroceryPhase.Planning, Plan = plan };
    }
}
