using Hound.Core.MarketIntel;

namespace Hound.Trading.Services.News;

/// <summary>
/// Fetches news for a symbol from the public Yahoo Finance headline RSS
/// endpoint. Yahoo's feed is symbol-scoped, so collision risk is low.
/// </summary>
internal sealed class YahooFinanceRssProvider : INewsProvider
{
    public string Name => "yahoofinance";

    private readonly IHttpClientFactory _httpClientFactory;

    public YahooFinanceRssProvider(IHttpClientFactory httpClientFactory)
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

        var url = $"https://feeds.finance.yahoo.com/rss/2.0/headline?s={Uri.EscapeDataString(symbol)}&region=US&lang=en-US";

        using var client = _httpClientFactory.CreateClient(NewsHttpClients.RssClientName);
        var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return RssParser.ParseRss20(body, "Yahoo Finance", symbol, lookback, maxItems);
    }
}
