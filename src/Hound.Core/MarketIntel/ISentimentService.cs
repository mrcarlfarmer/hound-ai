namespace Hound.Core.MarketIntel;

/// <summary>
/// Aggregate social-media sentiment snapshot for a symbol.
/// </summary>
public sealed record SentimentSnapshot(
    string Symbol,
    string Source,
    int Bullish,
    int Bearish,
    int Neutral,
    IReadOnlyList<string> RecentMessages,
    DateTimeOffset RetrievedAt)
{
    public int Total => Bullish + Bearish + Neutral;

    public static SentimentSnapshot Empty(string symbol, string source) =>
        new(symbol, source, 0, 0, 0, Array.Empty<string>(), DateTimeOffset.UtcNow);
}

/// <summary>
/// Retrieves a sentiment snapshot for a symbol. Implementations must never
/// throw — failures are absorbed and surfaced as an empty snapshot so the
/// caller always receives a usable result.
/// </summary>
public interface ISentimentService
{
    Task<SentimentSnapshot> GetSentimentAsync(
        string symbol,
        int maxMessages,
        CancellationToken cancellationToken = default);
}
