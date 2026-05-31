using Hound.Core.MarketIntel;
using Moq;

namespace Hound.Trading.Tests.Services;

[TestClass]
public sealed class NewsServiceTests
{
    private static NewsArticle Article(string source, string headline,
        DateTimeOffset? publishedAt = null) =>
        new(
            Source: source,
            Symbol: "AAPL",
            Headline: headline,
            Summary: null,
            Url: null,
            PublishedAt: publishedAt ?? DateTimeOffset.UtcNow);

    [TestMethod]
    public async Task GetRecentNewsAsync_NoProviders_ReturnsEmpty()
    {
        var service = new NewsService([]);

        var result = await service.GetRecentNewsAsync("AAPL", 10, TimeSpan.FromHours(24));

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task GetRecentNewsAsync_BlankSymbol_ReturnsEmpty()
    {
        var p = new Mock<INewsProvider>(MockBehavior.Strict);
        p.SetupGet(x => x.Name).Returns("P");
        var service = new NewsService([p.Object]);

        var result = await service.GetRecentNewsAsync("   ", 10, TimeSpan.FromHours(24));

        Assert.AreEqual(0, result.Count);
        p.Verify(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetRecentNewsAsync_MergesAndSortsDescending()
    {
        var older = Article("A", "First story", DateTimeOffset.UtcNow.AddHours(-3));
        var newer = Article("B", "Second story", DateTimeOffset.UtcNow.AddHours(-1));

        var p1 = StubProvider("P1", [older]);
        var p2 = StubProvider("P2", [newer]);

        var service = new NewsService([p1.Object, p2.Object]);

        var result = await service.GetRecentNewsAsync("AAPL", 10, TimeSpan.FromHours(24));

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("Second story", result[0].Headline);
        Assert.AreEqual("First story", result[1].Headline);
    }

    [TestMethod]
    public async Task GetRecentNewsAsync_DedupesByNormalisedHeadline()
    {
        var a = Article("A", "Apple Beats Earnings!", DateTimeOffset.UtcNow.AddHours(-2));
        var b = Article("B", "  apple beats earnings  ", DateTimeOffset.UtcNow.AddHours(-1));

        var service = new NewsService([
            StubProvider("A", [a]).Object,
            StubProvider("B", [b]).Object,
        ]);

        var result = await service.GetRecentNewsAsync("AAPL", 10, TimeSpan.FromHours(24));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("A", result[0].Source,
            "First-arriving provider should win the dedupe.");
    }

    [TestMethod]
    public async Task GetRecentNewsAsync_TrimsToMaxItems()
    {
        var articles = Enumerable.Range(0, 8)
            .Select(i => Article("P", $"Headline {i}", DateTimeOffset.UtcNow.AddMinutes(-i)))
            .ToList();

        var service = new NewsService([StubProvider("P", articles).Object]);

        var result = await service.GetRecentNewsAsync("AAPL", 3, TimeSpan.FromHours(24));

        Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public async Task GetRecentNewsAsync_OneProviderThrows_OthersStillReturn()
    {
        var good = StubProvider("Good", [Article("Good", "Good headline")]);
        var bad = new Mock<INewsProvider>();
        bad.SetupGet(x => x.Name).Returns("Bad");
        bad.Setup(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var service = new NewsService([good.Object, bad.Object]);

        var result = await service.GetRecentNewsAsync("AAPL", 10, TimeSpan.FromHours(24));

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Good headline", result[0].Headline);
    }

    [TestMethod]
    public async Task GetRecentNewsAsync_CancellationPropagates()
    {
        var p = new Mock<INewsProvider>();
        p.SetupGet(x => x.Name).Returns("P");
        p.Setup(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var service = new NewsService([p.Object]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => service.GetRecentNewsAsync("AAPL", 10, TimeSpan.FromHours(24), cts.Token));
    }

    private static Mock<INewsProvider> StubProvider(string name, IReadOnlyList<NewsArticle> articles)
    {
        var mock = new Mock<INewsProvider>();
        mock.SetupGet(x => x.Name).Returns(name);
        mock.Setup(x => x.FetchAsync(It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(articles);
        return mock;
    }
}
