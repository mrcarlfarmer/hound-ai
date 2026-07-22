namespace Hound.Trading.Services.News;

/// <summary>
/// Constants shared by all RSS-style news providers.
/// </summary>
internal static class NewsHttpClients
{
    /// <summary>
    /// Named <see cref="IHttpClientFactory"/> client used by every RSS news
    /// provider. Configured in <c>Program.cs</c> with a custom User-Agent and
    /// short timeout so a slow upstream never stalls the analyst pipeline.
    /// </summary>
    public const string RssClientName = "news-rss";
}
