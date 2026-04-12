using Hound.Api.Controllers;
using Hound.Api.Hubs;
using Hound.Core.Logging;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Hound.Api.Tests.Controllers;

[TestClass]
public class ActivityControllerTests
{
    private Mock<IActivityLogger> _mockLogger = null!;
    private Mock<IHubContext<ActivityHub>> _mockHubContext = null!;
    private Mock<IHubClients> _mockHubClients = null!;
    private Mock<IClientProxy> _mockClientProxy = null!;
    private ActivityController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<IActivityLogger>();
        _mockClientProxy = new Mock<IClientProxy>();
        _mockHubClients = new Mock<IHubClients>();
        _mockHubClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClientProxy.Object);
        _mockHubContext = new Mock<IHubContext<ActivityHub>>();
        _mockHubContext.Setup(h => h.Clients).Returns(_mockHubClients.Object);
        _controller = new ActivityController(_mockLogger.Object, _mockHubContext.Object);
    }

    [TestMethod]
    public async Task GetActivity_ReturnsOkWithResults()
    {
        var logs = new List<ActivityLog>
        {
            new() { Id = "logs/1", PackId = "trading", HoundName = "AnalysisHound", Message = "Signal detected" }
        };
        _mockLogger
            .Setup(l => l.GetActivitiesAsync(null, null, null, null, 1, 50, default))
            .ReturnsAsync(logs);

        var result = await _controller.GetActivity(cancellationToken: default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
    }

    [TestMethod]
    public async Task GetActivity_ReturnsEmptyList_WhenNoResults()
    {
        _mockLogger
            .Setup(l => l.GetActivitiesAsync(null, null, null, null, 1, 50, default))
            .ReturnsAsync(new List<ActivityLog>());

        var result = await _controller.GetActivity(cancellationToken: default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
        var value = ok.Value as IReadOnlyList<ActivityLog>;
        Assert.IsNotNull(value);
        Assert.AreEqual(0, value.Count);
    }

    [TestMethod]
    public async Task GetActivity_WithPackFilter_PassesPackIdToLogger()
    {
        _mockLogger
            .Setup(l => l.GetActivitiesAsync("trading", null, null, null, 1, 50, default))
            .ReturnsAsync(new List<ActivityLog>());

        var result = await _controller.GetActivity(pack: "trading", cancellationToken: default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        _mockLogger.Verify(
            l => l.GetActivitiesAsync("trading", null, null, null, 1, 50, default),
            Times.Once);
    }

    [TestMethod]
    public async Task GetActivity_WithHoundFilter_PassesHoundIdToLogger()
    {
        _mockLogger
            .Setup(l => l.GetActivitiesAsync(null, "analysis", null, null, 1, 50, default))
            .ReturnsAsync(new List<ActivityLog>());

        var result = await _controller.GetActivity(hound: "analysis", cancellationToken: default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        _mockLogger.Verify(
            l => l.GetActivitiesAsync(null, "analysis", null, null, 1, 50, default),
            Times.Once);
    }

    [TestMethod]
    public async Task GetActivity_WithAllFilters_PassesAllParametersToLogger()
    {
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        _mockLogger
            .Setup(l => l.GetActivitiesAsync("trading", "analysis", from, to, 2, 25, default))
            .ReturnsAsync(new List<ActivityLog>());

        var result = await _controller.GetActivity("trading", "analysis", from, to, 2, 25, default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        _mockLogger.Verify(
            l => l.GetActivitiesAsync("trading", "analysis", from, to, 2, 25, default),
            Times.Once);
    }

    [TestMethod]
    public async Task GetActivity_WithPagination_PassesPaginationToLogger()
    {
        _mockLogger
            .Setup(l => l.GetActivitiesAsync(null, null, null, null, 3, 10, default))
            .ReturnsAsync(new List<ActivityLog>());

        var result = await _controller.GetActivity(page: 3, pageSize: 10, cancellationToken: default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        _mockLogger.Verify(
            l => l.GetActivitiesAsync(null, null, null, null, 3, 10, default),
            Times.Once);
    }

    [TestMethod]
    public async Task PostActivity_PersistsActivityViaLogger()
    {
        var activity = new ActivityLog
        {
            PackId = "trading-pack",
            HoundId = "analysis-hound",
            HoundName = "AnalysisHound",
            Message = "Bullish signal on AAPL",
            Severity = ActivitySeverity.Info,
        };

        var result = await _controller.PostActivity(activity, default);

        _mockLogger.Verify(l => l.LogActivityAsync(activity, default), Times.Once);
        Assert.IsInstanceOfType<OkResult>(result);
    }

    [TestMethod]
    public async Task PostActivity_BroadcastsToPackSignalRGroup()
    {
        var activity = new ActivityLog
        {
            PackId = "trading-pack",
            HoundId = "strategy-hound",
            HoundName = "StrategyHound",
            Message = "Buy MSFT",
            Severity = ActivitySeverity.Success,
        };

        await _controller.PostActivity(activity, default);

        _mockHubClients.Verify(c => c.Group("pack-trading-pack"), Times.Once);
        _mockClientProxy.Verify(
            p => p.SendCoreAsync(
                "OnActivity",
                It.Is<object?[]>(args => args.Length == 1 && args[0] == activity),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task PostActivity_PersistsBeforeBroadcasting()
    {
        var callOrder = new List<string>();

        _mockLogger
            .Setup(l => l.LogActivityAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("persist"))
            .Returns(Task.CompletedTask);

        _mockClientProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("broadcast"))
            .Returns(Task.CompletedTask);

        var activity = new ActivityLog { PackId = "trading-pack", HoundId = "risk-hound" };
        await _controller.PostActivity(activity, default);

        Assert.AreEqual(2, callOrder.Count);
        Assert.AreEqual("persist", callOrder[0]);
        Assert.AreEqual("broadcast", callOrder[1]);
    }
}
