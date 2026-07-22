using Hound.Grocery.Nodes;
using Hound.Grocery.Services;

namespace Hound.Grocery.Eval;

// ── Deterministic, context-driven stubs ──────────────────────────────────────
// These let every grocery eval run FULLY OFFLINE (no Ollama, no live browser).
// The LLM/embedding/browser seams are replaced with canned responses supplied by
// the scenario's `context`, so both `--dry-run` (schema validation) and a full
// eval run need no network — unlike the trading harness, which requires Ollama.

/// <summary>
/// Fake <see cref="IBrowserWorkerClient"/> that serves search results from a
/// pre-seeded term→candidates map. Add/GetBasket return empty snapshots so
/// ShopperHound synthesises the basket from its own selections (its dry-run path).
/// Structurally it has no checkout/slot method — R1 is enforced by the interface.
/// </summary>
internal sealed class StubBrowserWorkerClient : IBrowserWorkerClient
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<ProductCandidate>> _byTerm;
    private readonly bool _authenticated;

    public StubBrowserWorkerClient(
        IReadOnlyDictionary<string, IReadOnlyList<ProductCandidate>> byTerm,
        bool authenticated = true)
    {
        _byTerm = byTerm;
        _authenticated = authenticated;
    }

    public Task<LoginResult> LoginAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new LoginResult(_authenticated, _authenticated ? "ok" : "no creds"));

    public Task<SearchResult> SearchAsync(string term, CancellationToken cancellationToken = default)
    {
        var candidates = _byTerm.TryGetValue(term, out var hits)
            ? hits
            : (IReadOnlyList<ProductCandidate>)Array.Empty<ProductCandidate>();
        return Task.FromResult(new SearchResult(term, candidates));
    }

    public Task<BasketSnapshot> AddToBasketAsync(string productId, double quantity, CancellationToken cancellationToken = default)
        => Task.FromResult(new BasketSnapshot(Array.Empty<BasketLine>(), 0m));

    public Task<BasketSnapshot> SetQuantityAsync(string productId, double quantity, CancellationToken cancellationToken = default)
        => Task.FromResult(new BasketSnapshot(Array.Empty<BasketLine>(), 0m));

    public Task<BasketSnapshot> GetBasketAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new BasketSnapshot(Array.Empty<BasketLine>(), 0m));

    public Task<FavouritesResult> GetFavouritesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new FavouritesResult(Array.Empty<ProductCandidate>()));

    public Task<OrderHistoryResult> GetOrderHistoryAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new OrderHistoryResult(Array.Empty<OrderHistoryEntry>()));
}

/// <summary>
/// Fake fuzzy matcher: returns a single canned <see cref="ItemMatch"/> when its
/// candidate is present in the supplied preference keys, else null (cold start).
/// </summary>
internal sealed class StubItemMatcher : IItemMatcher
{
    private readonly ItemMatch? _match;

    public StubItemMatcher(ItemMatch? match) => _match = match;

    public Task<ItemMatch?> FindBestMatchAsync(
        string item,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        if (_match is null) return Task.FromResult<ItemMatch?>(null);
        return Task.FromResult(candidates.Contains(_match.Candidate) ? _match : null);
    }
}

/// <summary>Fake cold-start assistant: returns a canned suggestion for every item.</summary>
internal sealed class StubPlannerAssistant : IPlannerAssistant
{
    private readonly PlanSuggestion _suggestion;

    public StubPlannerAssistant(PlanSuggestion suggestion) => _suggestion = suggestion;

    public Task<PlanSuggestion> SuggestAsync(string item, CancellationToken cancellationToken)
        => Task.FromResult(_suggestion with { SearchTerm = string.IsNullOrWhiteSpace(_suggestion.SearchTerm) ? item : _suggestion.SearchTerm });
}

/// <summary>
/// Fake NL parser: returns the scenario's canned intents. Simulates the
/// LlmShoppingListParser degrading a malformed/hostile message to
/// <see cref="ConciergeIntentKind.Unknown"/> without mutating the list.
/// </summary>
internal sealed class StubShoppingListParser : IShoppingListParser
{
    private readonly ShoppingListParseResult _result;

    public StubShoppingListParser(ShoppingListParseResult result) => _result = result;

    public Task<ShoppingListParseResult> ParseAsync(string message, CancellationToken cancellationToken)
        => Task.FromResult(_result);
}
