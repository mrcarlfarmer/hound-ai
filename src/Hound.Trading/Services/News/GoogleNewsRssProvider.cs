using Hound.Core.MarketIntel;

namespace Hound.Trading.Services.News;

/// <summary>
/// Fetches news for a symbol from the public Google News RSS endpoint. The
/// query is constructed as <c>"{symbol} stock"</c> so unrelated symbol
/// collisions (e.g. tickers that match common words) are minimised.
/// </summary>
internal sealed class GoogleNewsRssProvider : INewsProvider
{
    public string Name => "googlenews";

    private readonly IHttpClientFactory _httpClientFactory;

    public GoogleNewsRssProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<NewsArticle>> FetchAsync(
        string symbol,
        int maxItems,
        TimeSpan lookback,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return [];

        var query = Uri.EscapeDataString($"{symbol} stock");
        var url = $"https://news.google.com/rss/search?q={query}&hl=en-US&gl=US&ceid=US:en";

        using var client = _httpClientFactory.CreateClient(NewsHttpClients.RssClientName);
        var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return RssParser.ParseRss20(body, "Google News", symbol, lookback, maxItems);
    }
}
