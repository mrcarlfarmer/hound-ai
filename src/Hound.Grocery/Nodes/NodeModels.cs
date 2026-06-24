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
/// Outcome of <c>BudgetHound</c>'s pay-cycle reconciliation. When
/// <see cref="RequiresApproval"/> is set the pack flags the overage and asks the
/// user to approve or trim via Telegram (spec §9).
/// </summary>
public record BudgetVerdict(
    bool WithinBudget,
    decimal ProjectedWeeklyTotal,
    decimal ProjectedMonthlyTotal,
    bool RequiresApproval,
    string Reasoning);
