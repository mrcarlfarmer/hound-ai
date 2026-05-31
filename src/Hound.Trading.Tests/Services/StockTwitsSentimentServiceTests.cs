using Hound.Core.MarketIntel;
using Hound.Trading.Services;

namespace Hound.Trading.Tests.Services;

[TestClass]
public sealed class StockTwitsSentimentServiceTests
{
    [TestMethod]
    public void Parse_CountsBullishBearishNeutral()
    {
        var json = """
        {
            "messages": [
                { "body": "to the moon", "entities": { "sentiment": { "basic": "Bullish" } } },
                { "body": "all-time high incoming", "entities": { "sentiment": { "basic": "Bullish" } } },
                { "body": "dumping", "entities": { "sentiment": { "basic": "Bearish" } } },
                { "body": "no opinion" }
            ]
        }
        """;

        var snapshot = StockTwitsSentimentService.Parse(json, "AAPL", maxMessages: 10);

        Assert.AreEqual(2, snapshot.Bullish);
        Assert.AreEqual(1, snapshot.Bearish);
        Assert.AreEqual(1, snapshot.Neutral);
        Assert.AreEqual(4, snapshot.Total);
        Assert.AreEqual("StockTwits", snapshot.Source);
    }

    [TestMethod]
    public void Parse_RespectsMaxMessages()
    {
        var json = """
        {
            "messages": [
                { "body": "one" },
                { "body": "two" },
                { "body": "three" },
                { "body": "four" }
            ]
        }
        """;

        var snapshot = StockTwitsSentimentService.Parse(json, "AAPL", maxMessages: 2);

        Assert.AreEqual(2, snapshot.RecentMessages.Count);
        Assert.AreEqual(4, snapshot.Total);
    }

    [TestMethod]
    public void Parse_MissingMessagesArray_ReturnsEmpty()
    {
        var snapshot = StockTwitsSentimentService.Parse("{ \"symbol\": { \"id\": 1 } }",
            "AAPL", maxMessages: 5);

        Assert.AreEqual(0, snapshot.Total);
        Assert.AreEqual(0, snapshot.RecentMessages.Count);
    }

    [TestMethod]
    public void Parse_InvalidJson_ReturnsEmpty()
    {
        var snapshot = StockTwitsSentimentService.Parse("{ not json", "AAPL", maxMessages: 5);

        Assert.AreEqual(0, snapshot.Total);
    }
}
