using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Hound.Trading.Nodes;
using Microsoft.Extensions.AI;
using Moq;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace Hound.Trading.Tests.Nodes;

[TestClass]
public sealed class ExecutionNodeTests
{
    private Mock<IChatClient> _mockChatClient = null!;
    private Mock<IAlpacaService> _mockAlpacaService = null!;
    private Mock<IActivityLogger> _mockActivityLogger = null!;
    private Mock<IDocumentStore> _mockDocumentStore = null!;
    private Mock<IAsyncDocumentSession> _mockSession = null!;
    private ExecutionNode _node = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockChatClient = new Mock<IChatClient>();
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

        _node = new ExecutionNode(
            _mockChatClient.Object,
            _mockAlpacaService.Object,
            _mockActivityLogger.Object,
            _mockDocumentStore.Object);
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
            s => s.SubmitOrderAsync(It.IsAny<string>(), It.IsAny<Alpaca.Markets.OrderQuantity>(),
                It.IsAny<Alpaca.Markets.OrderSide>(), It.IsAny<Alpaca.Markets.OrderType>(),
                It.IsAny<Alpaca.Markets.TimeInForce>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()),
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

        SetupChatClientResponse("""{"success":true,"symbol":"AAPL","action":"Buy","quantity":50,"filledPrice":null,"orderId":"abc-123","message":"Order placed"}""");

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

        SetupChatClientResponse("""{"success":true,"symbol":"GOOG","action":"Buy","quantity":80,"filledPrice":null,"orderId":"def-456","message":"Order placed"}""");

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

        SetupChatClientResponse("""{"success":true,"symbol":"SPY","action":"Buy","quantity":10,"filledPrice":null,"orderId":"xyz-789","message":"Done"}""");

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

        SetupChatClientResponse("""{"success":true,"symbol":"AAPL","action":"Buy","quantity":10,"filledPrice":150.00,"orderId":"ord-001","message":"Filled"}""");

        var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
        var result = await _node.ExecuteAsync(state, default);

        Assert.AreEqual(GraphPhase.Monitor, result.Phase);
        Assert.IsFalse(result.IsComplete);
    }

    private void SetupChatClientResponse(string responseJson)
    {
        var chatResponse = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, responseJson)]);

        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        _mockSession
            .Setup(s => s.LoadAsync<TradeDocument>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeDocument { Id = "TradeDocuments/1" });
    }
}
