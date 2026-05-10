using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Hounds;
using Microsoft.Extensions.AI;
using Moq;

namespace Hound.Trading.Tests.Hounds;

[TestClass]
public sealed class RiskHoundTests
{
    private Mock<IChatClient> _mockChatClient = null!;
    private Mock<IAlpacaService> _mockAlpacaService = null!;
    private Mock<IActivityLogger> _mockActivityLogger = null!;
    private RiskHound _hound = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockChatClient = new Mock<IChatClient>();
        _mockAlpacaService = new Mock<IAlpacaService>();
        _mockActivityLogger = new Mock<IActivityLogger>();

        _mockActivityLogger
            .Setup(logger => logger.LogActivityAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _hound = new RiskHound(
            _mockChatClient.Object,
            _mockAlpacaService.Object,
            _mockActivityLogger.Object);
    }

    [TestMethod]
    public async Task EvaluateAsync_OrderExceedsMaximumShares_RejectsTrade()
    {
        SetupAccount(100_000m, 100_000m, 50_000m);
        SetupPositions();

        var decision = new TradingDecision("NVDA", TradeAction.Buy, 2_000m, "Oversized order", 0.72);

        var assessment = await _hound.EvaluateAsync(decision);

        Assert.AreEqual(RiskVerdict.Rejected, assessment.Verdict);
        Assert.AreEqual(decision, assessment.Decision);
        Assert.IsTrue(assessment.Reasoning.Contains("hard limit", StringComparison.OrdinalIgnoreCase));
        Assert.IsNull(assessment.AdjustedQuantity);
        _mockAlpacaService.Verify(
            service => service.GetBarsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BarTimeFrame>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockActivityLogger.Verify(
            logger => logger.LogActivityAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [TestMethod]
    public async Task EvaluateAsync_DrawdownBreachedForBuy_RejectsTrade()
    {
        SetupAccount(85_000m, 100_000m, 60_000m);
        SetupPositions();

        var decision = new TradingDecision("AAPL", TradeAction.Buy, 10m, "Add to winning position", 0.81);

        var assessment = await _hound.EvaluateAsync(decision);

        Assert.AreEqual(RiskVerdict.Rejected, assessment.Verdict);
        Assert.IsTrue(assessment.Reasoning.Contains("drawdown", StringComparison.OrdinalIgnoreCase));
        Assert.IsNull(assessment.AdjustedQuantity);
        _mockAlpacaService.Verify(
            service => service.GetBarsAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<BarTimeFrame>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task EvaluateAsync_BuyExceedsPositionCapacity_ModifiesQuantity()
    {
        SetupAccount(100_000m, 100_000m, 50_000m);
        SetupPositions(CreatePosition("AAPL", quantity: 50m, availableQuantity: 50m, marketValue: 10_000m, currentPrice: 200m));

        var decision = new TradingDecision("AAPL", TradeAction.Buy, 60m, "Scale into momentum", 0.88);

        var assessment = await _hound.EvaluateAsync(decision);

        Assert.AreEqual(RiskVerdict.Modified, assessment.Verdict);
        Assert.AreEqual(50m, assessment.AdjustedQuantity);
        Assert.IsTrue(assessment.Reasoning.Contains("20% position limit", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task EvaluateAsync_SellExceedsAvailableQuantity_ModifiesToAvailablePosition()
    {
        SetupAccount(100_000m, 100_000m, 20_000m);
        SetupPositions(CreatePosition("MSFT", quantity: 40m, availableQuantity: 40m, marketValue: 12_000m, currentPrice: 300m));

        var decision = new TradingDecision("MSFT", TradeAction.Sell, 75m, "Trim position", 0.60);

        var assessment = await _hound.EvaluateAsync(decision);

        Assert.AreEqual(RiskVerdict.Modified, assessment.Verdict);
        Assert.AreEqual(40m, assessment.AdjustedQuantity);
        Assert.IsTrue(assessment.Reasoning.Contains("available position size", StringComparison.OrdinalIgnoreCase));
    }

    private void SetupAccount(decimal equity, decimal lastEquity, decimal tradableCash)
    {
        var account = new Mock<IAccount>();
        account.SetupGet(value => value.Equity).Returns(equity);
        account.SetupGet(value => value.LastEquity).Returns(lastEquity);
        account.SetupGet(value => value.TradableCash).Returns(tradableCash);

        _mockAlpacaService
            .Setup(service => service.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account.Object);
    }

    private void SetupPositions(params IPosition[] positions)
    {
        _mockAlpacaService
            .Setup(service => service.ListPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);
    }

    private static IPosition CreatePosition(
        string symbol,
        decimal quantity,
        decimal availableQuantity,
        decimal marketValue,
        decimal currentPrice)
    {
        var position = new Mock<IPosition>();
        position.SetupGet(value => value.Symbol).Returns(symbol);
        position.SetupGet(value => value.Quantity).Returns(quantity);
        position.SetupGet(value => value.AvailableQuantity).Returns(availableQuantity);
        position.SetupGet(value => value.MarketValue).Returns((decimal?)marketValue);
        position.SetupGet(value => value.AssetCurrentPrice).Returns((decimal?)currentPrice);
        return position.Object;
    }
}
