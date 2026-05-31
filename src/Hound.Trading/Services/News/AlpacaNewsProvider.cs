using Hound.Core.MarketIntel;
using Hound.Trading.AlpacaClient;

namespace Hound.Trading.Services.News;

/// <summary>
/// Pulls historical news articles from the Alpaca Data API via
/// <see cref="IAlpacaService.ListNewsAsync"/>. Reuses paper-trading
/// credentials — no extra configuration required.
/// </summary>
internal sealed class AlpacaNewsProvider : INewsProvider
{
    public string Name => "alpaca";

    private readonly IAlpacaService _alpacaService;

    public AlpacaNewsProvider(IAlpacaService alpacaService)
    {
        _alpacaService = alpacaService;
    }

    public async Task<IReadOnlyList<NewsArticle>> FetchAsync(
        string symbol,
        int maxItems,
        TimeSpan lookback,
        CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow - lookback;
        var articles = await _alpacaService.ListNewsAsync([symbol], since, maxItems, cancellationToken);

        var result = new List<NewsArticle>(articles.Count);
        foreach (var a in articles)
        {
            if (string.IsNullOrWhiteSpace(a.Headline)) continue;
            result.Add(new NewsArticle(
                Source: string.IsNullOrWhiteSpace(a.Source) ? "Alpaca" : a.Source!,
                Symbol: symbol,
                Headline: a.Headline,
                Summary: string.IsNullOrWhiteSpace(a.Summary) ? null : a.Summary,
                Url: a.ArticleUrl?.ToString(),
                PublishedAt: new DateTimeOffset(a.UpdatedAtUtc, TimeSpan.Zero),
                Author: string.IsNullOrWhiteSpace(a.Author) ? null : a.Author));
        }

        return result;
    }
}
