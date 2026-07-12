using System.Globalization;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Graph;
using Hound.Grocery.Services;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Nodes;

/// <summary>
/// Reconciles the scraped basket against the pay-cycle budget (spec §9). All
/// maths is deterministic .NET — no LLM. BudgetHound's job is to compute a
/// verdict, record the spend in <c>budget-ledger.md</c> and flag-and-ask on any
/// overage (R2/R3); it never trims, refuses or blocks checkout.
/// </summary>
public class BudgetHound : INode
{
    public string NodeId => "budget-hound";
    public string PackId => GroceryPack.PackId;

    private static readonly BasketResult EmptyBasket = new([], 0m);

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
        var basket = state.Basket ?? EmptyBasket;
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        var verdict = await ReconcileAsync(basket, asOf, cancellationToken);

        return state with
        {
            CurrentNode = NodeId,
            Phase = GroceryPhase.Budgeting,
            BudgetVerdict = verdict,
        };
    }

    /// <summary>
    /// Deterministically reconciles <paramref name="basket"/> against the budget
    /// as of <paramref name="asOf"/>, records the build in the ledger and logs a
    /// <c>BudgetChecked</c> activity. Exposed internally so tests can drive a
    /// fixed date without <see cref="DateTime.UtcNow"/> nondeterminism.
    /// </summary>
    internal async Task<BudgetVerdict> ReconcileAsync(BasketResult basket, DateOnly asOf, CancellationToken cancellationToken)
    {
        var entries = await _ledger.LoadAsync(cancellationToken);
        var (priorWeekSpend, priorCycleSpend) = _ledger.SpendToDate(entries, asOf);

        var verdict = _ledger.EvaluateBasket(basket.Subtotal, priorWeekSpend, priorCycleSpend);

        _logger?.LogInformation(
            "BudgetHound reconciled basket {Subtotal} → {Outcome} (week {Week}, cycle {Cycle}).",
            basket.Subtotal, verdict.Outcome, verdict.ProjectedWeeklyTotal, verdict.ProjectedCycleTotal);

        await _ledger.RecordBuildAsync(
            new BudgetLedgerEntry(
                Date: asOf,
                CycleStart: _ledger.CurrentCycleStart(asOf),
                WeekStart: _ledger.CurrentWeekStart(asOf),
                BasketSubtotal: basket.Subtotal,
                Outcome: verdict.Outcome.ToString()),
            cancellationToken);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(BudgetHound),
            Message = verdict.Summary,
            Severity = verdict.RequiresApproval ? ActivitySeverity.Warning : ActivitySeverity.Success,
            Metadata = new Dictionary<string, object>
            {
                ["event"] = "BudgetChecked",
                ["outcome"] = verdict.Outcome.ToString(),
                ["basketSubtotal"] = Format(verdict.BasketSubtotal),
                ["weeklyTarget"] = Format(verdict.WeeklyTarget),
                ["weeklyThreshold"] = Format(verdict.WeeklyThreshold),
                ["projectedWeeklyTotal"] = Format(verdict.ProjectedWeeklyTotal),
                ["weeklyOverage"] = Format(verdict.WeeklyOverage),
                ["weeklyPercentOver"] = verdict.WeeklyPercentOver,
                ["monthlyCap"] = Format(verdict.MonthlyCap),
                ["projectedCycleTotal"] = Format(verdict.ProjectedCycleTotal),
                ["cycleOverage"] = Format(verdict.CycleOverage),
                ["cyclePercentOver"] = verdict.CyclePercentOver,
                ["requiresApproval"] = verdict.RequiresApproval,
            },
        }, cancellationToken);

        return verdict;
    }

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
