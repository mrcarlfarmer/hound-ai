using Hound.Core.Models;

namespace Hound.Core.Tests.Models;

[TestClass]
public sealed class TradeDocumentTests
{
    [TestMethod]
    public void TradeDocument_Defaults_AreInitialized()
    {
        var trade = new TradeDocument();

        Assert.AreEqual(string.Empty, trade.Id);
        Assert.AreEqual(string.Empty, trade.Symbol);
        Assert.AreEqual(string.Empty, trade.Action);
        Assert.AreEqual(0m, trade.RequestedQuantity);
        Assert.AreEqual(string.Empty, trade.OrderId);
        Assert.AreEqual(FillStatus.Pending, trade.FillStatus);
        Assert.AreEqual(0m, trade.FilledQuantity);
        Assert.IsNull(trade.AverageFillPrice);
        Assert.IsNull(trade.ExecutionTime);
        Assert.AreEqual(string.Empty, trade.RiskAssessmentSummary);
        Assert.AreEqual(string.Empty, trade.PackId);
        Assert.AreEqual(string.Empty, trade.HoundId);
    }

    [TestMethod]
    public void TradeDocument_DefaultTimestamps_AreRecentUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var trade = new TradeDocument();
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.IsTrue(trade.CreatedAt >= before && trade.CreatedAt <= after);
        Assert.IsTrue(trade.UpdatedAt >= before && trade.UpdatedAt <= after);
    }

    [TestMethod]
    public void TradeDocument_SetProperties_ReflectsValues()
    {
        var createdAt = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddMinutes(5);
        var executionTime = updatedAt.AddMinutes(1);

        var trade = new TradeDocument
        {
            Id = "trades/1",
            Symbol = "AAPL",
            Action = "Buy",
            RequestedQuantity = 10m,
            OrderId = "order-1",
            FillStatus = FillStatus.Filled,
            FilledQuantity = 10m,
            AverageFillPrice = 185.25m,
            ExecutionTime = executionTime,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            RiskAssessmentSummary = "Within risk limits",
            PackId = "trading-pack",
            HoundId = "execution-hound"
        };

        Assert.AreEqual("trades/1", trade.Id);
        Assert.AreEqual("AAPL", trade.Symbol);
        Assert.AreEqual("Buy", trade.Action);
        Assert.AreEqual(10m, trade.RequestedQuantity);
        Assert.AreEqual("order-1", trade.OrderId);
        Assert.AreEqual(FillStatus.Filled, trade.FillStatus);
        Assert.AreEqual(10m, trade.FilledQuantity);
        Assert.AreEqual(185.25m, trade.AverageFillPrice);
        Assert.AreEqual(executionTime, trade.ExecutionTime);
        Assert.AreEqual(createdAt, trade.CreatedAt);
        Assert.AreEqual(updatedAt, trade.UpdatedAt);
        Assert.AreEqual("Within risk limits", trade.RiskAssessmentSummary);
        Assert.AreEqual("trading-pack", trade.PackId);
        Assert.AreEqual("execution-hound", trade.HoundId);
    }

    [TestMethod]
    public void FillStatus_AllValues_Defined()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(FillStatus), FillStatus.Pending));
        Assert.IsTrue(Enum.IsDefined(typeof(FillStatus), FillStatus.PartiallyFilled));
        Assert.IsTrue(Enum.IsDefined(typeof(FillStatus), FillStatus.Filled));
        Assert.IsTrue(Enum.IsDefined(typeof(FillStatus), FillStatus.Canceled));
        Assert.IsTrue(Enum.IsDefined(typeof(FillStatus), FillStatus.Expired));
        Assert.IsTrue(Enum.IsDefined(typeof(FillStatus), FillStatus.Rejected));
    }
}
