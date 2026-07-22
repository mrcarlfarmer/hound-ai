using Microsoft.Extensions.Logging;

namespace Hound.Core.MarketIntel;

/// <summary>
/// Aggregator that fans out a news request to all registered
/// <see cref="INewsProvider"/> implementations in parallel, dedupes by
/// normalised headline, and returns the most recent articles. Per-provider
/// failures are logged and absorbed.
/// </summary>
public sealed class NewsService : INewsService
{
    private readonly IReadOnlyList<INewsProvider> _providers;
    private readonly ILogger<NewsService>? _logger;

    public NewsService(
        IEnumerable<INewsProvider> providers,
        ILogger<NewsService>? logger = null)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    public async Task<IReadOnlyList<NewsArticle>> GetRecentNewsAsync(
        string symbol,
        int maxItems,
        TimeSpan lookback,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol) || maxItems <= 0 || _providers.Count == 0)
            return [];

        // Ask each provider for the full quota so we have room to dedupe
        // before trimming. Fan out in parallel so the slowest provider
        // doesn't dominate latency.
        var tasks = _providers
            .Select(p => FetchSafelyAsync(p, symbol, maxItems, lookback, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<NewsArticle>(maxItems * _providers.Count);

        foreach (var batch in results)
        {
            foreach (var article in batch)
            {
                var key = NormaliseHeadline(article.Headline);
                if (key.Length == 0) continue;
                if (seen.Add(key))
                    merged.Add(article);
            }
        }

        return merged
            .OrderByDescending(a => a.PublishedAt)
            .Take(maxItems)
            .ToList();
    }

    private async Task<IReadOnlyList<NewsArticle>> FetchSafelyAsync(
        INewsProvider provider,
        string symbol,
        int maxItems,
        TimeSpan lookback,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.FetchAsync(symbol, maxItems, lookback, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "News provider {Provider} failed for {Symbol}; continuing with other providers.",
                provider.Name, symbol);
            return [];
        }
    }

    private static string NormaliseHeadline(string headline)
    {
        if (string.IsNullOrWhiteSpace(headline)) return string.Empty;
        var span = headline.AsSpan().Trim();
        Span<char> buffer = stackalloc char[span.Length];
        var i = 0;
        var lastWasSpace = false;
        foreach (var c in span)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[i++] = char.ToLowerInvariant(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                buffer[i++] = ' ';
                lastWasSpace = true;
            }
        }
        return new string(buffer[..i]).Trim();
    }
}
