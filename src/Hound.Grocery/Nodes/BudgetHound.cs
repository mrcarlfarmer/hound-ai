using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Graph;
using Hound.Grocery.Services;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Nodes;

/// <summary>
/// Maintains the pay-cycle budget ledger, computes weekly/monthly projected
/// totals, applies the ±10% weekly flex and drives the flag-and-ask decision
/// (spec §9). Mostly deterministic with light LLM use for messaging.
/// <para>
/// <b>Phase 1 scaffold:</b> returns a benign "within budget" verdict
/// placeholder. No ledger maths or LLM calls are wired up yet (Phase 5).
/// </para>
/// </summary>
public class BudgetHound : INode
{
    public string NodeId => "budget-hound";
    public string PackId => GroceryPack.PackId;

    private readonly IActivityLogger _activityLogger;
    private readonly BudgetLedgerService _ledger;
    private readonly ILogger<BudgetHound>? _logger;

    public BudgetHound(
        IActivityLogger activityLogger,
        BudgetLedgerService ledger,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _ledger = ledger;
        _logger = loggerFactory?.CreateLogger<BudgetHound>();
    }

    public async Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("BudgetHound stub invoked for run {RunId}", state.RunId);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(BudgetHound),
            Message = "BudgetHound scaffold placeholder (Phase 1 — no behaviour yet)",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var verdict = new BudgetVerdict(
            WithinBudget: true,
            ProjectedWeeklyTotal: 0m,
            ProjectedMonthlyTotal: 0m,
            RequiresApproval: false,
            Reasoning: "Scaffold placeholder verdict (Phase 1).");

        return state with { CurrentNode = NodeId, Phase = GroceryPhase.Budgeting, BudgetVerdict = verdict };
    }
}
