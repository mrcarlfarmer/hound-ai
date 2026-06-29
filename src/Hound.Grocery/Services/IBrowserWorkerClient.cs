using Hound.Grocery.Nodes;

namespace Hound.Grocery.Services;

// ── RPC DTOs for the grocery-browser sidecar (spec §11.3) ────────────────────

/// <summary>Request to add a product to the basket.</summary>
public record AddToBasketRequest(string ProductId, double Quantity);

/// <summary>Request to set a product's basket quantity via the +/- counter.</summary>
public record SetQuantityRequest(string ProductId, double Quantity);

/// <summary>Request to search the Sainsbury's catalogue.</summary>
public record SearchRequest(string Term);

/// <summary>Result of ensuring an authenticated session.</summary>
public record LoginResult(bool Authenticated, string? Message);

/// <summary>
/// A ranked candidate product returned by <c>/search</c>. The sidecar owns the
/// DOM + vision; the .NET ShopperHound only ranks/selects.
/// </summary>
public record ProductCandidate(
    string ProductId,
    string Name,
    decimal Price,
    decimal? NectarPrice,
    bool IsNectarPrice,
    bool IsFavourite,
    bool InStock,
    string? PerUnitPrice,
    string Url,
    string? ImgRef)
{
    /// <summary>
    /// The price used for ranking/budgeting: the Nectar/loyalty price when the
    /// item carries one, otherwise the standard retail price.
    /// </summary>
    public decimal EffectivePrice => IsNectarPrice && NectarPrice is { } np ? np : Price;
}

/// <summary>Ranked candidates for a search term.</summary>
public record SearchResult(string Term, IReadOnlyList<ProductCandidate> Candidates);

/// <summary>The user's saved favourites, scraped from /groceries/favourites.</summary>
public record FavouritesResult(IReadOnlyList<ProductCandidate> Candidates);

/// <summary>Current basket lines + subtotal.</summary>
public record BasketSnapshot(IReadOnlyList<BasketLine> Lines, decimal Subtotal);

/// <summary>A past order summary (for learning).</summary>
public record OrderHistoryEntry(string OrderId, DateTime PlacedAt, decimal Total);

/// <summary>Recent orders scraped for learning.</summary>
public record OrderHistoryResult(IReadOnlyList<OrderHistoryEntry> Orders);

/// <summary>
/// Thin typed client over the <c>grocery-browser</c> FastAPI sidecar. The
/// sidecar owns the undetected Chrome browser and the safety interlocks (URL
/// denylist + action allowlist) that enforce R1 (never select a slot, never
/// check out).
/// </summary>
public interface IBrowserWorkerClient
{
    Task<LoginResult> LoginAsync(CancellationToken cancellationToken = default);
    Task<SearchResult> SearchAsync(string term, CancellationToken cancellationToken = default);
    Task<BasketSnapshot> AddToBasketAsync(string productId, double quantity, CancellationToken cancellationToken = default);
    Task<BasketSnapshot> SetQuantityAsync(string productId, double quantity, CancellationToken cancellationToken = default);
    Task<BasketSnapshot> GetBasketAsync(CancellationToken cancellationToken = default);
    Task<FavouritesResult> GetFavouritesAsync(CancellationToken cancellationToken = default);
    Task<OrderHistoryResult> GetOrderHistoryAsync(CancellationToken cancellationToken = default);
}
