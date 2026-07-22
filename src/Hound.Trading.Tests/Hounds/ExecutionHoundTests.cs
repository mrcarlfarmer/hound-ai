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
    /// <summary>The TradeDocument captured at StoreAsync, returned by LoadAsync so tests can assert persisted stop state.</summary>
    private TradeDocument? _storedTrade;

    [TestInitialize]
    public void Setup()
    {
        _mockAlpacaService = new Mock<IAlpacaService>();
        _mockActivityLogger = new Mock<IActivityLogger>();
        _mockDocumentStore = new Mock<IDocumentStore>();
        _mockSession = new Mock<IAsyncDocumentSession>();
        _storedTrade = null;

        _mockDocumentStore
            .Setup(s => s.OpenAsyncSession(It.IsAny<string>()))
            .Returns(_mockSession.Object);

        _mockSession
            .Setup(s => s.StoreAsync(It.IsAny<TradeDocument>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((entity, _) =>
            {
                if (entity is TradeDocument doc)
                {
                    if (string.IsNullOrEmpty(doc.Id))
                        doc.Id = $"TradeDocuments/{Guid.NewGuid():N}";
                    _storedTrade = doc;
                }
            })
            .Returns(Task.CompletedTask);

        // Return the captured document (post-creation, with StopMode etc.) so
        // the node's final update mutates the same instance the test inspects.
        _mockSession
            .Setup(s => s.LoadAsync<TradeDocument>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _storedTrade ?? new TradeDocument { Id = "TradeDocuments/1" });

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
        Assert.AreEqual(GraphPhase.Entry, result.Phase);
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
        Assert.AreEqual(GraphPhase.Entry, result.Phase);
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

    // ---------------------------------------------------------------------
    // Trailing-stop protective exit on Buy: every accepted Buy must trigger
    // a follow-up SubmitTrailingStopOrderAsync(Sell, GTC) using the
    // strategy's chosen trail percent (defaulting to 5% when omitted). Direct
    // Sells stay on the plain market path; failure of the protective stop
    // must not mark the Buy as failed.
    // ---------------------------------------------------------------------

    [TestMethod]
    public async Task ExecuteAsync_BuyAction_AttachesTrailingStopSellWithStrategyTrailPercent()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 5, "Bullish entry", 0.9, TrailPercent: 3m),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("AAPL", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        _mockAlpacaService
            .Setup(s => s.SubmitTrailingStopOrderAsync(
                "AAPL", It.IsAny<OrderQuantity>(), OrderSide.Sell, 3m, TimeInForce.Gtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.IsTrue(result.ExecutionOutput!.Success);

        _mockAlpacaService.Verify(
            s => s.SubmitTrailingStopOrderAsync(
                "AAPL", It.IsAny<OrderQuantity>(), OrderSide.Sell, 3m, TimeInForce.Gtc, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_BuyAction_WithoutTrailPercent_DefaultsToFivePercent()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("MSFT", TradeAction.Buy, 2, "Bullish", 0.8),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("MSFT", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        _mockAlpacaService
            .Setup(s => s.SubmitTrailingStopOrderAsync(
                "MSFT", It.IsAny<OrderQuantity>(), OrderSide.Sell,
                StrategyNode.DefaultBuyTrailPercent, TimeInForce.Gtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("MSFT") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.IsTrue(result.ExecutionOutput!.Success);

        _mockAlpacaService.Verify(
            s => s.SubmitTrailingStopOrderAsync(
                "MSFT", It.IsAny<OrderQuantity>(), OrderSide.Sell,
                StrategyNode.DefaultBuyTrailPercent, TimeInForce.Gtc, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_SellAction_DoesNotUseTrailingStopPath()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Sell, 10, "Take profits", 0.9),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("AAPL", It.IsAny<OrderQuantity>(), OrderSide.Sell,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        await _node.ExecuteAsync(state, default);

        _mockAlpacaService.Verify(
            s => s.SubmitTrailingStopOrderAsync(It.IsAny<string>(), It.IsAny<OrderQuantity>(),
                It.IsAny<OrderSide>(), It.IsAny<decimal>(), It.IsAny<TimeInForce>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ExecuteAsync_BuyFailsAtBroker_DoesNotSubmitTrailingStop()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 5, "Bullish", 0.9),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("AAPL", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Rejected).Object);

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.IsFalse(result.ExecutionOutput!.Success);

        _mockAlpacaService.Verify(
            s => s.SubmitTrailingStopOrderAsync(It.IsAny<string>(), It.IsAny<OrderQuantity>(),
                It.IsAny<OrderSide>(), It.IsAny<decimal>(), It.IsAny<TimeInForce>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ExecuteAsync_BuySucceeds_TrailingStopThrows_StillReportsBuySuccess()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 5, "Bullish", 0.9),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("AAPL", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        _mockAlpacaService
            .Setup(s => s.SubmitTrailingStopOrderAsync(It.IsAny<string>(), It.IsAny<OrderQuantity>(),
                It.IsAny<OrderSide>(), It.IsAny<decimal>(), It.IsAny<TimeInForce>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker rejected protective stop"));

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        // The Buy itself still succeeds — the protective stop failure is a
        // warning, not a failure of the trade.
        Assert.IsTrue(result.ExecutionOutput!.Success);

        _mockActivityLogger.Verify(
            l => l.LogActivityAsync(
                It.Is<ActivityLog>(a =>
                    a.Severity == ActivitySeverity.Warning &&
                    a.Message.Contains("trailing-stop exit submission failed")),
                default),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Fractional positions: Alpaca categorically rejects stop / trailing-stop
    // orders on fractional quantities (fractional orders are Day-only
    // market/limit-only, no stops). ExecutionNode keeps the fractional Buy,
    // skips the broker stop, and records SoftwareTrailing state so the
    // SoftwareStopPoller can emulate a trailing stop instead.
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_BuyFractionalQuantity_DoesNotSubmitTrailingStop()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("GOOG", TradeAction.Buy, 0.3315m, "Bullish", 0.75, TrailPercent: 5m),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("GOOG", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("GOOG") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        // Buy itself succeeded.
        Assert.IsTrue(result.ExecutionOutput!.Success);

        // Broker-side trailing-stop submission must be skipped — the broker
        // would reject it because the position is fractional.
        _mockAlpacaService.Verify(
            s => s.SubmitTrailingStopOrderAsync(It.IsAny<string>(), It.IsAny<OrderQuantity>(),
                It.IsAny<OrderSide>(), It.IsAny<decimal>(), It.IsAny<TimeInForce>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Instead we emit an Info activity announcing the software-stop fallback
        // so reviewers know the position is still protected.
        _mockActivityLogger.Verify(
            l => l.LogActivityAsync(
                It.Is<ActivityLog>(a =>
                    a.Severity == ActivitySeverity.Info &&
                    a.Message.Contains("software trailing stop")),
                default),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_BuyWholeShareQuantity_SubmitsTrailingStop()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 7m, "Bullish", 0.9, TrailPercent: 4m),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("AAPL", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        _mockAlpacaService
            .Setup(s => s.SubmitTrailingStopOrderAsync(
                "AAPL", It.IsAny<OrderQuantity>(), OrderSide.Sell, 4m, TimeInForce.Gtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        await _node.ExecuteAsync(state, default);

        // Whole-share quantity (7) is broker-eligible for a trailing stop.
        _mockAlpacaService.Verify(
            s => s.SubmitTrailingStopOrderAsync(
                "AAPL", It.IsAny<OrderQuantity>(), OrderSide.Sell, 4m, TimeInForce.Gtc, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------------------
    // Tiered quantity normalisation + stop-mode persistence.
    // ---------------------------------------------------------------------

    [TestMethod]
    public async Task ExecuteAsync_Buy_QuantityWithFraction_RoundsDownAndAttachesTrailingStop()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("GOOG", TradeAction.Buy, 3.5m, "Bullish", 0.85, TrailPercent: 4m),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("GOOG", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted, avgFill: 100m).Object);

        _mockAlpacaService
            .Setup(s => s.SubmitTrailingStopOrderAsync(
                "GOOG", It.IsAny<OrderQuantity>(), OrderSide.Sell, 4m, TimeInForce.Gtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("GOOG") with { RiskOutput = assessment };
        await _node.ExecuteAsync(state, default);

        // Entry order placed for the rounded-down whole-share quantity (3).
        _mockAlpacaService.Verify(
            s => s.SubmitOrderAsync("GOOG", It.Is<OrderQuantity>(q => q.Value == 3m), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Broker-side trailing stop attached for the same whole-share quantity.
        _mockAlpacaService.Verify(
            s => s.SubmitTrailingStopOrderAsync(
                "GOOG", It.Is<OrderQuantity>(q => q.Value == 3m), OrderSide.Sell, 4m, TimeInForce.Gtc, It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.IsNotNull(_storedTrade);
        Assert.AreEqual(StopMode.BrokerTrailing, _storedTrade!.StopMode);
        Assert.AreEqual(3m, _storedTrade.RequestedQuantity);
    }

    [TestMethod]
    public async Task ExecuteAsync_Buy_QuantityExactlyOne_AttachesBrokerTrailingStop()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("AAPL", TradeAction.Buy, 1m, "Bullish", 0.9, TrailPercent: 5m),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("AAPL", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted, avgFill: 200m).Object);

        _mockAlpacaService
            .Setup(s => s.SubmitTrailingStopOrderAsync(
                "AAPL", It.IsAny<OrderQuantity>(), OrderSide.Sell, 5m, TimeInForce.Gtc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted).Object);

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        await _node.ExecuteAsync(state, default);

        _mockAlpacaService.Verify(
            s => s.SubmitTrailingStopOrderAsync(
                "AAPL", It.IsAny<OrderQuantity>(), OrderSide.Sell, 5m, TimeInForce.Gtc, It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.IsNotNull(_storedTrade);
        Assert.AreEqual(StopMode.BrokerTrailing, _storedTrade!.StopMode);
    }

    [TestMethod]
    public async Task ExecuteAsync_Buy_FractionalQuantity_PersistsSoftwareStopState()
    {
        var assessment = new RiskAssessment(
            RiskVerdict.Approved,
            new TradingDecision("GOOG", TradeAction.Buy, 0.3315m, "Bullish", 0.75, TrailPercent: 5m),
            "Approved");

        _mockAlpacaService
            .Setup(s => s.SubmitOrderAsync("GOOG", It.IsAny<OrderQuantity>(), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MockOrder(OrderStatus.Accepted, avgFill: 100m).Object);

        var state = TradingGraphState.Initial("GOOG") with { RiskOutput = assessment };
        await _node.ExecuteAsync(state, default);

        // Fractional Buy submitted as-is (no rounding).
        _mockAlpacaService.Verify(
            s => s.SubmitOrderAsync("GOOG", It.Is<OrderQuantity>(q => q.Value == 0.3315m), OrderSide.Buy,
                OrderType.Market, TimeInForce.Day, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Software-stop state persisted for the poller to act on.
        Assert.IsNotNull(_storedTrade);
        Assert.AreEqual(StopMode.SoftwareTrailing, _storedTrade!.StopMode);
        Assert.AreEqual(5m, _storedTrade.TrailPercent);
        Assert.AreEqual(100m, _storedTrade.EntryPrice);
        Assert.AreEqual(95m, _storedTrade.StopPrice); // 100 * (1 - 5/100)
    }
}
