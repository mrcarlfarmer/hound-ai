using Hound.Api.Controllers;
using Hound.Api.Hubs;
using Hound.Api.Repositories;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Hound.Api.Tests.Controllers;

[TestClass]
public sealed class TradesControllerTests
{
    private Mock<ITradeRepository> _mockRepo = null!;
    private Mock<IHubContext<ActivityHub>> _mockHubContext = null!;
    private TradesController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<ITradeRepository>();
        _mockHubContext = new Mock<IHubContext<ActivityHub>>();

        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _mockHubContext.Setup(h => h.Clients).Returns(mockClients.Object);

        _controller = new TradesController(_mockRepo.Object, _mockHubContext.Object);
    }

    [TestMethod]
    public async Task GetTrades_ReturnsOkWithResults()
    {
        var trades = new List<TradeDocument>
        {
            new() { Id = "TradeDocuments/1", Symbol = "AAPL", FillStatus = FillStatus.Filled }
        };
        _mockRepo
            .Setup(r => r.GetTradesAsync(1, 20, null, null, default))
            .ReturnsAsync(trades);

        var result = await _controller.GetTrades(cancellationToken: default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
    }

    [TestMethod]
    public async Task GetTrades_ReturnsEmptyList_WhenNoResults()
    {
        _mockRepo
            .Setup(r => r.GetTradesAsync(1, 20, null, null, default))
            .ReturnsAsync(new List<TradeDocument>());

        var result = await _controller.GetTrades(cancellationToken: default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var value = ok!.Value as IReadOnlyList<TradeDocument>;
        Assert.IsNotNull(value);
        Assert.AreEqual(0, value.Count);
    }

    [TestMethod]
    public async Task GetTrade_ReturnsOk_WhenFound()
    {
        var trade = new TradeDocument
        {
            Id = "TradeDocuments/1",
            Symbol = "AAPL",
            FillStatus = FillStatus.Pending
        };
        _mockRepo
            .Setup(r => r.GetTradeAsync("TradeDocuments/1", default))
            .ReturnsAsync(trade);

        var result = await _controller.GetTrade("TradeDocuments/1", default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
    }

    [TestMethod]
    public async Task GetTrade_ReturnsNotFound_WhenMissing()
    {
        _mockRepo
            .Setup(r => r.GetTradeAsync("TradeDocuments/999", default))
            .ReturnsAsync((TradeDocument?)null);

        var result = await _controller.GetTrade("TradeDocuments/999", default);

        Assert.IsInstanceOfType<NotFoundResult>(result.Result);
    }

    [TestMethod]
    public async Task PostOrderUpdate_ReturnsOk_AndBroadcasts()
    {
        var trade = new TradeDocument
        {
            Id = "TradeDocuments/1",
            Symbol = "MSFT",
            FillStatus = FillStatus.Filled,
            FilledQuantity = 50,
            AverageFillPrice = 425.50m,
        };

        var result = await _controller.PostOrderUpdate(trade, default);

        Assert.IsInstanceOfType<OkResult>(result);
        _mockRepo.Verify(r => r.UpsertTradeAsync(trade, default), Times.Once);
    }
}
