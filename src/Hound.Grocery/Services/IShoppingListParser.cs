namespace Hound.Grocery.Services;

/// <summary>
/// The kind of change a user's natural-language message requests for the loose
/// shopping list (spec §7.1).
/// </summary>
public enum ConciergeIntentKind
{
    /// <summary>Add an item (optionally with a quantity).</summary>
    Add,

    /// <summary>Remove an item.</summary>
    Remove,

    /// <summary>Set the quantity of an item to a specific value.</summary>
    SetQuantity,

    /// <summary>Ask a question (e.g. "what's on the list?").</summary>
    Query,

    /// <summary>Message could not be understood as a grocery instruction.</summary>
    Unknown,
}

/// <summary>
/// A single parsed instruction. A message may yield several (e.g. "add milk and
/// eggs" → two <see cref="ConciergeIntentKind.Add"/> intents).
/// </summary>
public record ConciergeIntent(
    ConciergeIntentKind Kind,
    string? ItemName = null,
    double? Quantity = null,
    string? QueryText = null);

/// <summary>The full set of intents extracted from one user message.</summary>
public record ShoppingListParseResult(IReadOnlyList<ConciergeIntent> Intents)
{
    public static ShoppingListParseResult Unknown { get; } =
        new([new ConciergeIntent(ConciergeIntentKind.Unknown)]);
}

/// <summary>
/// Turns an untrusted natural-language Telegram message into structured
/// <see cref="ConciergeIntent"/>s. The LLM-backed implementation
/// (<see cref="LlmShoppingListParser"/>) injects the persona system prompt and a
/// prompt-injection guard; tests substitute a fake.
/// </summary>
public interface IShoppingListParser
{
    Task<ShoppingListParseResult> ParseAsync(string message, CancellationToken cancellationToken);
}
