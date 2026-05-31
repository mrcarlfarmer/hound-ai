using Hound.Core.MarketIntel;
using Hound.Trading.Services.News;

namespace Hound.Trading.Tests.Services;

[TestClass]
public sealed class RssParserTests
{
    [TestMethod]
    public void ParseRss20_ValidFeed_ReturnsArticles()
    {
        var now = DateTimeOffset.UtcNow.ToString("R");
        var xml = $"""
        <rss version="2.0">
          <channel>
            <title>Test feed</title>
            <item>
              <title>Acme posts Q3 results</title>
              <link>https://example.test/a</link>
              <description>Revenue up 12% &amp; margin steady</description>
              <pubDate>{now}</pubDate>
            </item>
            <item>
              <title>Acme CEO interview</title>
              <link>https://example.test/b</link>
              <description>&lt;p&gt;CEO speaks&lt;/p&gt;</description>
              <pubDate>{now}</pubDate>
            </item>
          </channel>
        </rss>
        """;

        var articles = RssParser.ParseRss20(xml, "Test", "ACME",
            TimeSpan.FromHours(24), maxItems: 10);

        Assert.AreEqual(2, articles.Count);
        Assert.AreEqual("Test", articles[0].Source);
        Assert.AreEqual("ACME", articles[0].Symbol);
        Assert.AreEqual("Acme posts Q3 results", articles[0].Headline);
        Assert.AreEqual("Revenue up 12% & margin steady", articles[0].Summary);
        Assert.AreEqual("https://example.test/a", articles[0].Url);
        Assert.AreEqual("CEO speaks", articles[1].Summary);
    }

    [TestMethod]
    public void ParseRss20_OlderThanLookback_Filtered()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-7).ToString("R");
        var fresh = DateTimeOffset.UtcNow.AddHours(-1).ToString("R");
        var xml = $"""
        <rss>
          <channel>
            <item><title>Old story</title><pubDate>{old}</pubDate></item>
            <item><title>Fresh story</title><pubDate>{fresh}</pubDate></item>
          </channel>
        </rss>
        """;

        var articles = RssParser.ParseRss20(xml, "Test", "AAPL",
            TimeSpan.FromHours(24), maxItems: 10);

        Assert.AreEqual(1, articles.Count);
        Assert.AreEqual("Fresh story", articles[0].Headline);
    }

    [TestMethod]
    public void ParseRss20_RespectsMaxItems()
    {
        var fresh = DateTimeOffset.UtcNow.ToString("R");
        var items = string.Concat(Enumerable.Range(0, 5).Select(i =>
            $"<item><title>Story {i}</title><pubDate>{fresh}</pubDate></item>"));
        var xml = $"<rss><channel>{items}</channel></rss>";

        var articles = RssParser.ParseRss20(xml, "Test", "AAPL",
            TimeSpan.FromHours(24), maxItems: 2);

        Assert.AreEqual(2, articles.Count);
    }

    [TestMethod]
    public void ParseRss20_EmptyOrInvalid_ReturnsEmpty()
    {
        Assert.AreEqual(0, RssParser.ParseRss20("", "Test", "AAPL",
            TimeSpan.FromHours(24), 10).Count);
        Assert.AreEqual(0, RssParser.ParseRss20("not xml", "Test", "AAPL",
            TimeSpan.FromHours(24), 10).Count);
    }
}
