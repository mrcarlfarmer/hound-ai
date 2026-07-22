using System.Globalization;
using System.Xml.Linq;
using Hound.Core.MarketIntel;

namespace Hound.Trading.Services.News;

/// <summary>
/// Shared helpers for parsing RSS feeds into <see cref="NewsArticle"/>
/// records. Targets simple RSS 2.0 / Atom feeds used by Google News and
/// Yahoo Finance.
/// </summary>
internal static class RssParser
{
    /// <summary>
    /// Parses an RSS 2.0 feed body into news articles. Returns an empty list
    /// when the body is malformed.
    /// </summary>
    public static IReadOnlyList<NewsArticle> ParseRss20(
        string body, string source, string symbol, TimeSpan lookback, int maxItems)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        XDocument doc;
        try
        {
            doc = XDocument.Parse(body);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var items = doc.Descendants("item");
        var cutoff = DateTimeOffset.UtcNow - lookback;
        var articles = new List<NewsArticle>();

        foreach (var item in items)
        {
            var title = item.Element("title")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(title)) continue;

            var link = item.Element("link")?.Value?.Trim();
            var description = item.Element("description")?.Value?.Trim();
            var pubDateRaw = item.Element("pubDate")?.Value?.Trim();

            if (!TryParseRssDate(pubDateRaw, out var publishedAt))
                publishedAt = DateTimeOffset.UtcNow;

            if (publishedAt < cutoff) continue;

            articles.Add(new NewsArticle(
                Source: source,
                Symbol: symbol,
                Headline: title,
                Summary: StripHtml(description),
                Url: link,
                PublishedAt: publishedAt));

            if (articles.Count >= maxItems) break;
        }

        return articles;
    }

    private static bool TryParseRssDate(string? raw, out DateTimeOffset value)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
            return true;

        // RFC 1123 fallback for feeds that omit the seconds component.
        var formats = new[]
        {
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, dd MMM yyyy HH:mm zzz",
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
        };
        return DateTimeOffset.TryParseExact(raw, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    private static string? StripHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var sb = new System.Text.StringBuilder(input.Length);
        var inTag = false;
        foreach (var c in input)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        var cleaned = sb.ToString()
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&#39;", "'", StringComparison.Ordinal)
            .Replace("&#x27;", "'", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
