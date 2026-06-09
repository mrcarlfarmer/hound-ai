using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Services;
using Microsoft.Extensions.Options;
using Moq;
using Raven.Client.Documents;

namespace Hound.Trading.Tests.Services;

[TestClass]
public sealed class SoftwareStopPollerTests
{
    private static SoftwareStopPoller CreatePoller(
        Mock<IAlpacaService> alpaca,
        Mock<IDocumentStore> store,
        SoftwareStopSettings? settings = null) =>
        new(
            alpaca.Object,
            store.Object,
            Mock.Of<IActivityLogger>(),
            Options.Create(settings ?? new SoftwareStopSettings()));

    private static TradeDocument SoftwareStopDoc(
        decimal? entryPrice = 100m,
        decimal? highWaterMark = null,
        decimal? trailPercent = 5m) =>
        new()
        {
            Id = "TradeDocuments/1",
            Symbol = "GOOG",
            Action = "Buy",
            StopMode = StopMode.SoftwareTrailing,
            FillStatus = FillStatus.Filled,
            FilledQuantity = 0.3315m,
            TrailPercent = trailPercent,
            EntryPrice = entryPrice,
            HighWaterMark = highWaterMark,
        };

    // ── Pure stop math (ComputeStopUpdate) ────────────────────────────────────

    [TestMethod]
    public void ComputeStopUpdate_PriceAboveHighWaterMark_AdvancesHwmAndStop()
    {
        var doc = SoftwareStopDoc(entryPrice: 100m, highWaterMark: 100m, trailPercent: 5m);

        var result = SoftwareStopPoller.ComputeStopUpdate(doc, lastPrice: 120m);

        Assert.AreEqual(120m, result.HighWaterMark);
        Assert.AreEqual(114m, result.StopPrice); // 120 * (1 - 5/100)
        Assert.IsFalse(result.Triggered);
    }

    [TestMethod]
    public void ComputeStopUpdate_PriceBetweenStopAndHwm_DoesNotTriggerOrLowerHwm()
    {
        var doc = SoftwareStopDoc(entryPrice: 100m, highWaterMark: 120m, trailPercent: 5m);

        // Price pulled back to 116 — below the 120 HWM but above the 114 stop.
        var result = SoftwareStopPoller.ComputeStopUpdate(doc, lastPrice: 116m);

        Assert.AreEqual(120m, result.HighWaterMark); // never lowered
        Assert.AreEqual(114m, result.StopPrice);
        Assert.IsFalse(result.Triggered);
    }

    [TestMethod]
    public void ComputeStopUpdate_PriceAtStop_Triggers()
    {
        var doc = SoftwareStopDoc(entryPrice: 100m, highWaterMark: 120m, trailPercent: 5m);

        var result = SoftwareStopPoller.ComputeStopUpdate(doc, lastPrice: 114m);

        Assert.IsTrue(result.Triggered);
    }

    [TestMethod]
    public void ComputeStopUpdate_PriceBelowStop_Triggers()
    {
        var doc = SoftwareStopDoc(entryPrice: 100m, highWaterMark: 120m, trailPercent: 5m);

        var result = SoftwareStopPoller.ComputeStopUpdate(doc, lastPrice: 110m);

        Assert.IsTrue(result.Triggered);
    }

    [TestMethod]
    public void ComputeStopUpdate_NullHighWaterMark_InitialisesFromEntryPrice()
    {
        var doc = SoftwareStopDoc(entryPrice: 100m, highWaterMark: null, trailPercent: 5m);

        var result = SoftwareStopPoller.ComputeStopUpdate(doc, lastPrice: 100m);

        Assert.AreEqual(100m, result.HighWaterMark);
        Assert.AreEqual(95m, result.StopPrice);
        Assert.IsFalse(result.Triggered);
    }

    [TestMethod]
    public void ComputeStopUpdate_NullHwmAndEntryPrice_InitialisesFromLastPrice()
    {
        var doc = SoftwareStopDoc(entryPrice: null, highWaterMark: null, trailPercent: 5m);

        var result = SoftwareStopPoller.ComputeStopUpdate(doc, lastPrice: 50m);

        Assert.AreEqual(50m, result.HighWaterMark);
        Assert.AreEqual(47.5m, result.StopPrice); // 50 * (1 - 5/100)
        Assert.IsFalse(result.Triggered);
    }

    [TestMethod]
    public void ComputeStopUpdate_NullTrailPercent_UsesDefault()
    {
        var doc = SoftwareStopDoc(entryPrice: 100m, highWaterMark: 100m, trailPercent: null);

        var result = SoftwareStopPoller.ComputeStopUpdate(doc, lastPrice: 100m);

        // StrategyNode.DefaultBuyTrailPercent == 5
        Assert.AreEqual(95m, result.StopPrice);
    }

    // ── Cycle orchestration ───────────────────────────────────────────────────

    [TestMethod]
    public async Task RunCycleAsync_MarketClosed_DoesNothing()
    {
        var alpaca = new Mock<IAlpacaService>();
        alpaca.Setup(a => a.GetClockAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockClock(isOpen: false));

        var store = new Mock<IDocumentStore>();
        var poller = CreatePoller(alpaca, store);

        await poller.RunCycleAsync(default);

        // Closed market: no position or price lookups, no session opened.
        alpaca.Verify(a => a.ListPositionsAsync(It.IsAny<CancellationToken>()), Times.Never);
        alpaca.Verify(a => a.GetLatestTradeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.OpenAsyncSession(It.IsAny<string>()), Times.Never);
    }

    [TestMethod]
    public async Task RunCycleAsync_RunWhenMarketClosed_SkipsClockCheck()
    {
        var alpaca = new Mock<IAlpacaService>();
        var store = new Mock<IDocumentStore>();

        // With RunWhenMarketClosed the poller must NOT call the clock and must
        // proceed to open a session for the candidate query. We throw from
        // OpenAsyncSession to prove the code reached that point without needing
        // a full RavenDB query mock.
        store.Setup(s => s.OpenAsyncSession(It.IsAny<string>()))
            .Throws(new InvalidOperationException("reached-query"));

        var poller = CreatePoller(alpaca, store, new SoftwareStopSettings { RunWhenMarketClosed = true });

        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => poller.RunCycleAsync(default));

        Assert.AreEqual("reached-query", ex.Message);
        alpaca.Verify(a => a.GetClockAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IClock MockClock(bool isOpen)
    {
        var clock = new Mock<IClock>();
        clock.Setup(c => c.IsOpen).Returns(isOpen);
        clock.Setup(c => c.TimestampUtc).Returns(DateTime.UtcNow);
        return clock.Object;
    }
}
