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
/// A concrete, actionable item produced by <c>PlannerHound</c> from a loose
/// list entry: a search term, the learned preferred product (if any) and a
/// target quantity.
/// </summary>
public record PlannedItem(
    string SearchTerm,
    string? PreferredProduct,
    double TargetQuantity);

/// <summary>The full plan emitted by <c>PlannerHound</c>.</summary>
public record ShoppingPlan(
    IReadOnlyList<PlannedItem> Items);

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
