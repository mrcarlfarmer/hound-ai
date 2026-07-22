using Hound.Core.MarketIntel;

namespace Hound.Eval;

/// <summary>
/// Eval-harness stub <see cref="INewsService"/>. Returns no articles by
/// default so scenarios can run without network access; symbols containing
/// "NEWS" yield a single synthetic headline so news-aware scenarios can
/// verify the analyst formats real data correctly.
/// </summary>
internal sealed class StubNewsService : INewsService
{
    public Task<IReadOnlyList<NewsArticle>> GetRecentNewsAsync(
        string symbol,
        int maxItems,
        TimeSpan lookback,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol) ||
            symbol.Contains("NONEWS", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<NewsArticle>>(Array.Empty<NewsArticle>());
        }

        var article = new NewsArticle(
            Source: "EvalStub",
            Symbol: symbol,
            Headline: $"{symbol} posts in-line earnings, raises full-year guidance",
            Summary: $"Synthetic eval news item for {symbol}.",
            Url: $"https://example.test/news/{symbol}",
            PublishedAt: DateTimeOffset.UtcNow.AddHours(-2));

        return Task.FromResult<IReadOnlyList<NewsArticle>>(new[] { article });
    }
}

/// <summary>
/// Eval-harness stub <see cref="ISentimentService"/>. Returns an empty
/// snapshot by default; symbols containing "BULL"/"BEAR" yield a tagged
/// snapshot so sentiment-aware scenarios can verify formatting.
/// </summary>
internal sealed class StubSentimentService : ISentimentService
{
    public Task<SentimentSnapshot> GetSentimentAsync(
        string symbol,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return Task.FromResult(SentimentSnapshot.Empty(symbol ?? string.Empty, "EvalStub"));

        if (symbol.Contains("BULL", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new SentimentSnapshot(
                Symbol: symbol,
                Source: "EvalStub",
                Bullish: 8,
                Bearish: 1,
                Neutral: 1,
                RecentMessages: new[] { "Loving the breakout volume here.", "Calls printing." },
                RetrievedAt: DateTimeOffset.UtcNow));
        }

        if (symbol.Contains("BEAR", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new SentimentSnapshot(
                Symbol: symbol,
                Source: "EvalStub",
                Bullish: 1,
                Bearish: 7,
                Neutral: 2,
                RecentMessages: new[] { "Distribution all day.", "Headed back to support." },
                RetrievedAt: DateTimeOffset.UtcNow));
        }

        return Task.FromResult(SentimentSnapshot.Empty(symbol, "EvalStub"));
    }
}
