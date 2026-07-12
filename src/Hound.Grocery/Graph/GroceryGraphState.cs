using Hound.Grocery.Nodes;

namespace Hound.Grocery.Graph;

/// <summary>
/// How a scheduled basket build was triggered.
/// </summary>
public enum GroceryTrigger
{
    Scheduled,
    OnDemand,
}

/// <summary>
/// Phase of the scheduled basket-build flow (spec §7.2). The flow always ends
/// with the basket populated — it <b>never</b> advances to slot selection or
/// checkout (hard safety rule R1).
/// </summary>
public enum GroceryPhase
{
    Entry,
    Planning,
    Shopping,
    Budgeting,
    Reporting,
    Learning,
    Complete,
}

/// <summary>
/// Immutable state record that flows through the grocery graph. Each node
/// returns <c>state with { ... }</c> to produce the next snapshot.
/// <para>
/// Phase 1 scaffold: the slots exist so downstream phases can populate them; no
/// node currently produces real LLM- or browser-derived data.
/// </para>
/// </summary>
public record GroceryGraphState
{
    public required string RunId { get; init; }
    public GroceryTrigger Trigger { get; init; } = GroceryTrigger.Scheduled;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public string? CurrentNode { get; init; }
    public GroceryPhase Phase { get; init; } = GroceryPhase.Entry;

    // ── Node output slots ────────────────────────────────────────────────────
    public ShoppingPlan? Plan { get; init; }
    public BasketResult? Basket { get; init; }
    public BudgetVerdict? BudgetVerdict { get; init; }

    // ── Terminal flags ───────────────────────────────────────────────────────
    public bool IsComplete { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Creates the initial state for a new basket-build run.</summary>
    public static GroceryGraphState Initial(GroceryTrigger trigger = GroceryTrigger.Scheduled) => new()
    {
        RunId = $"grocery-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}",
        Trigger = trigger,
    };
}
