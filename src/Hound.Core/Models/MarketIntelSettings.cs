namespace Hound.Core.Models;

/// <summary>
/// Settings controlling how the news analyst retrieves market news.
/// Bound from the <c>News</c> configuration section.
/// </summary>
public class NewsSettings
{
    public const string SectionName = "News";

    /// <summary>
    /// Maximum number of news articles to surface to the analyst per fetch.
    /// </summary>
    public int MaxItems { get; set; } = 10;

    /// <summary>
    /// How far back to look for news articles, in hours.
    /// </summary>
    public int LookbackHours { get; set; } = 48;

    /// <summary>
    /// Per-HTTP-call timeout (seconds) for RSS providers.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Allow-list of provider names (case-insensitive) to enable. When empty
    /// or null, every registered <c>INewsProvider</c> is used. Provider names
    /// are the lowercase short identifiers returned by
    /// <c>INewsProvider.Name</c> — e.g. <c>"alpaca"</c>, <c>"googlenews"</c>,
    /// <c>"yahoofinance"</c>.
    /// </summary>
    public List<string> Providers { get; set; } = [];
}

/// <summary>
/// Settings controlling how the sentiment analyst retrieves social media
/// sentiment. Bound from the <c>Sentiment</c> configuration section.
/// </summary>
public class SentimentSettings
{
    public const string SectionName = "Sentiment";

    /// <summary>
    /// Maximum number of recent messages to surface in the sentiment summary.
    /// </summary>
    public int MaxMessages { get; set; } = 10;

    /// <summary>
    /// Per-HTTP-call timeout (seconds) for sentiment providers.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Allow-list of provider names (case-insensitive) to enable. When empty
    /// or null, every registered sentiment provider is used. Provider names
    /// are the lowercase short identifiers — currently only <c>"stocktwits"</c>.
    /// </summary>
    public List<string> Providers { get; set; } = [];
}
