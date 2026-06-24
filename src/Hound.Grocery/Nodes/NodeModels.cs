namespace Hound.Grocery.Nodes;

// ── Typed record DTOs for the grocery graph ──────────────────────────────────
// Mirrors Hound.Trading/Nodes/NodeModels.cs. Phase 1 scaffold: the shapes are
// defined so downstream phases can populate them; nodes do not yet produce real
// data.

/// <summary>A single loose item on the shared shopping list.</summary>
public record ShoppingListItem(
    string Name,
    double Quantity,
    string AddedBy,
    DateTime AddedAt);

/// <summary>
/// Relative importance of a planned item, used downstream to decide trim order
/// when the basket is over budget (BudgetHound, Phase 5). Staples and explicit
/// favourites plan at <see cref="High"/>; everything else at <see cref="Normal"/>.
/// </summary>
public enum PlanPriority
{
    Low,
    Normal,
    High,
}

/// <summary>
/// A concrete, actionable item produced by <c>PlannerHound</c> from a loose
/// list entry (spec §6, §10): the original list text, the search term to run on
/// the Sainsbury's site, the learned preferred product (if any), a target
/// quantity, a planning priority and the favourite/staple flags that influence
/// ranking and trim order.
/// </summary>
public record PlannedItem(
    string RawListText,
    string SearchTerm,
    string? PreferredProduct,
    double TargetQuantity,
    PlanPriority Priority = PlanPriority.Normal,
    bool IsFavourite = false,
    bool IsStaple = false);

/// <summary>
/// The full plan emitted by <c>PlannerHound</c>. <see cref="WeeklyBudgetTarget"/>
/// carries loose budget context (the configured weekly target) so the plan can be
/// budget-aware; full reconciliation remains BudgetHound's job (Phase 5).
/// </summary>
public record ShoppingPlan(
    IReadOnlyList<PlannedItem> Items,
    decimal? WeeklyBudgetTarget = null,
    DateTime GeneratedAt = default);

/// <summary>
/// One line added to the live Sainsbury's basket by <c>ShopperHound</c>, with
/// the flags required for the audit trace (R4).
/// </summary>
public record BasketLine(
    string ProductId,
    string ProductName,
    double Quantity,
    decimal Price,
    bool IsNectarPrice,
    bool IsFavourite,
    string? SubstitutionReason);

/// <summary>The result of a basket build: the lines added and the subtotal.</summary>
public record BasketResult(
    IReadOnlyList<BasketLine> Lines,
    decimal Subtotal);

/// <summary>
/// How a basket reconciles against the budget (spec §9). Enforcement is soft —
/// the human checks out — so anything other than <see cref="Ok"/> drives a
/// flag-and-ask over Telegram rather than a hard block.
/// </summary>
public enum BudgetOutcome
{
    /// <summary>Within the weekly flex band and under the pay-cycle cap.</summary>
    Ok,

    /// <summary>Projected weekly total exceeds the target + flex.</summary>
    WeeklyOver,

    /// <summary>Projected pay-cycle total exceeds the monthly cap.</summary>
    CycleOver,

    /// <summary>Both the weekly band and the pay-cycle cap are exceeded.</summary>
    Both,
}

/// <summary>
/// Outcome of <c>BudgetHound</c>'s pay-cycle reconciliation (spec §9). Carries
/// the figures the Concierge needs to relay a clear approve/trim question. When
/// <see cref="RequiresApproval"/> is set the pack flags the overage and asks the
/// user — it never refuses, because the human completes checkout.
/// </summary>
public record BudgetVerdict(
    BudgetOutcome Outcome,
    decimal BasketSubtotal,
    decimal WeeklyTarget,
    decimal WeeklyThreshold,
    decimal ProjectedWeeklyTotal,
    decimal WeeklyOverage,
    double WeeklyPercentOver,
    decimal MonthlyCap,
    decimal ProjectedCycleTotal,
    decimal CycleOverage,
    double CyclePercentOver,
    bool RequiresApproval,
    string Summary)
{
    /// <summary>Convenience flag: <c>true</c> only when the outcome is <see cref="BudgetOutcome.Ok"/>.</summary>
    public bool WithinBudget => Outcome == BudgetOutcome.Ok;
}
