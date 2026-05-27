using Alpaca.Markets;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Hound.Trading.Nodes;
using Moq;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace Hound.Trading.Tests.Nodes;

[TestClass]
public sealed class ExecutionNodeTests
{
    private Mock<IAlpacaService> _mockAlpacaService = null!;
    private Mock<IActivityLogger> _mockActivityLogger = null!;
    private Mock<IDocumentStore> _mockDocumentStore = null!;
    private Mock<IAsyncDocumentSession> _mockSession = null!;
    private ExecutionNode _node = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockAlpacaService = new Mock<IAlpacaService>();
        _mockActivityLogger = new Mock<IActivityLogger>();
        _mockDocumentStore = new Mock<IDocumentStore>();
        _mockSession = new Mock<IAsyncDocumentSession>();

        _mockDocumentStore
            .Setup(s => s.OpenAsyncSession(It.IsAny<string>()))
            .Returns(_mockSession.Object);

        _mockSession
            .Setup(s => s.StoreAsync(It.IsAny<TradeDocument>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((entity, _) =>
            {
                if (entity is TradeDocument doc && string.IsNullOrEmpty(doc.Id))
                    doc.Id = $"TradeDocuments/{Guid.NewGuid():N}";
            })
            .Returns(Task.CompletedTask);

        _mockSession
            .Setup(s => s.LoadAsync<TradeDocument>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeDocument { Id = "TradeDocuments/1" });

        _node = new ExecutionNode(
            _mockAlpacaService.Object,
            _mockActivityLogger.Object,
            _mockDocumentStore.Object);
    }

    private static Mock<IOrder> MockOrder(OrderStatus status, Guid? orderId = null, decimal? avgFill = null)
    {
        var m = new Mock<IOrder>();
        m.Setup(o => o.OrderId).Returns(orderId ?? Guid.NewGuid());
        m.Setup(o => o.OrderStatus).Returns(status);
        m.Setup(o => o.AverageFillPrice).Returns(avgFill);
        return m;
    }

    [TestMethod]
    public async Task ExecuteAsync_RejectedAssessment_ReturnsFailure()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Rejected,
            new TradingDecision("AAPL", TradeAction.Buy, 50, "Test", 0.9),
            "Position too large");

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.IsFalse(result.ExecutionOutput!.Success);
        Assert.AreEqual("AAPL", result.ExecutionOutput.Symbol);
        Assert.IsTrue(result.ExecutionOutput.Message.Contains("Rejected"));
        _mockActivityLogger.Verify(
            l => l.LogActivityAsync(It.Is<ActivityLog>(a => a.Severity == ActivitySeverity.Warning), default),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_RejectedAssessment_DoesNotPlaceOrder()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Rejected,
            new TradingDecision("AAPL", TradeAction.Buy, 50, "Test", 0.9),
            "Position too large");

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        await _node.ExecuteAsync(state, default);

        _mockAlpacaService.Verify(
            s => s.SubmitOrderAsync(It.IsAny<string>(), It.IsAny<OrderQuantity>(),
                It.IsAny<OrderSide>(), It.IsAny<OrderType>(),
                It.IsAny<TimeInForce>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ExecuteAsync_RejectedAssessment_DoesNotCreateTradeDocument()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Rejected,
            new TradingDecision("MSFT", TradeAction.Sell, 100, "Test", 0.8),
            "Max exposure exceeded");

        var state = TradingGraphState.Initial("MSFT") with { RiskOutput = assessment };
        await _node.ExecuteAsync(state, default);

        _mockSession.Verify(
            s => s.StoreAsync(It.IsAny<TradeDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ExecuteAsync_ApprovedAssessment_CreatesTradeDocument()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 50, "Bullish", 0.9),
            "Within risk limits");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("AAPL", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        await _node.ExecuteAsync(state, default);

        _mockSession.Verify(
            s => s.StoreAsync(It.Is<TradeDocument>(t =>
                t.Symbol == "AAPL" &&
                t.Action == "Buy" &&
                t.RequestedQuantity == 50 &&
                t.FillStatus == FillStatus.Pending),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_ModifiedAssessment_UsesAdjustedQuantity()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Modified,
            new TradingDecision("GOOG", TradeAction.Buy, 200, "Bullish", 0.75),
            "Adjusted to 80 shares",
            AdjustedQuantity: 80);

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("GOOG", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("GOOG") with { RiskOutput = assessment };
        await _node.ExecuteAsync(state, default);

        _mockSession.Verify(
            s => s.StoreAsync(It.Is<TradeDocument>(t => t.RequestedQuantity == 80),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_ApprovedAssessment_ReturnsTradeDocumentId()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("SPY", TradeAction.Buy, 10, "Bullish", 0.85),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("SPY", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("SPY") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.IsNotNull(result.ExecutionOutput!.TradeDocumentId);
        Assert.AreNotEqual(string.Empty, result.ExecutionOutput.TradeDocumentId);
    }

    [TestMethod]
    public async Task ExecuteAsync_SuccessfulExecution_TransitionsToMonitorPhase()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 10, "Bullish", 0.85),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("AAPL", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted, avgFill: 150m).Object);

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.IsTrue(result.ExecutionOutput!.Success);
        Assert.AreEqual(GraphPhase.Monitor, result.Phase);
        Assert.IsFalse(result.IsComplete);
    }

    [TestMethod]
    public async Task ExecuteAsync_AlpacaThrows_ReturnsFailureAndCompletes()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 10, "Bullish", 0.85),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync(It.IsAny<string>(), It.IsAny<OrderQuantity>(),
                It.IsAny<OrderSide>(), It.IsAny<OrderType>(),
                It.IsAny<TimeInForce>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Alpaca down"));

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.IsFalse(result.ExecutionOutput!.Success);
        Assert.AreEqual(string.Empty, result.ExecutionOutput.OrderId);
        Assert.IsTrue(result.IsComplete);
        Assert.AreNotEqual(GraphPhase.Monitor, result.Phase);
        _mockActivityLogger.Verify(
            l => l.LogActivityAsync(It.Is<ActivityLog>(a => a.Severity == ActivitySeverity.Error), default),
            Times.AtLeastOnce);
    }

    [TestMethod]
    public async Task ExecuteAsync_OrderRejectedByBroker_ReturnsFailure()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 10, "Bullish", 0.85),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync(It.IsAny<string>(), It.IsAny<OrderQuantity>(),
                It.IsAny<OrderSide>(), It.IsAny<OrderType>(),
                It.IsAny<TimeInForce>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Rejected).Object);

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.IsFalse(result.ExecutionOutput!.Success);
        Assert.IsTrue(result.IsComplete);
        Assert.AreNotEqual(GraphPhase.Monitor, result.Phase);
    }

    [TestMethod]
    public async Task ExecuteAsync_EmptyOrderIdFromBroker_ReturnsFailure()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 10, "Bullish", 0.85),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync(It.IsAny<string>(), It.IsAny<OrderQuantity>(),
                It.IsAny<OrderSide>(), It.IsAny<OrderType>(),
                It.IsAny<TimeInForce>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted, orderId: Guid.Empty).Object);

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.IsFalse(result.ExecutionOutput!.Success);
        Assert.AreEqual(string.Empty, result.ExecutionOutput.OrderId);
        Assert.IsTrue(result.IsComplete);
    }
}
