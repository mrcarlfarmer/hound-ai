namespace Hound.Core.MarketIntel;

/// <summary>
/// Provider-agnostic view of a single market news article. Returned by every
/// <see cref="INewsProvider"/> implementation and surfaced to consumers via
/// <see cref="INewsService"/>.
/// </summary>
public sealed record NewsArticle(
    string Source,
    string Symbol,
    string Headline,
    string? Summary,
    string? Url,
    DateTimeOffset PublishedAt,
    string? Author = null);

/// <summary>
/// Aggregates news articles from one or more providers for a given symbol.
/// Implementations must never throw — per-provider failures are absorbed so
/// downstream consumers always receive a (possibly empty) result.
/// </summary>
public interface INewsService
{
    Task<IReadOnlyList<NewsArticle>> GetRecentNewsAsync(
        string symbol,
        int maxItems,
        TimeSpan lookback,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Single source of news articles for a symbol. Implementations should
/// catch and log their own errors and return an empty list on failure.
/// </summary>
public interface INewsProvider
{
    /// <summary>
    /// Stable provider identifier (case-insensitive) used by configuration
    /// to enable or disable individual sources. Should be short and
    /// lowercase-friendly — e.g. <c>"alpaca"</c>, <c>"googlenews"</c>.
    /// </summary>
    string Name { get; }

    Task<IReadOnlyList<NewsArticle>> FetchAsync(
        string symbol,
        int maxItems,
        TimeSpan lookback,
        CancellationToken cancellationToken);
}
