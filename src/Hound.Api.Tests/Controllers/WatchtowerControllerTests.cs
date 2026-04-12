using Hound.Api.Controllers;
using Hound.Api.Hubs;
using Hound.Api.Repositories;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;
using System.Text.Json;

namespace Hound.Api.Tests.Controllers;

[TestClass]
public sealed class WatchtowerControllerTests
{
    private Mock<IWatchtowerRepository> _repoMock = null!;
    private Mock<IHubContext<ActivityHub>> _hubMock = null!;
    private WatchtowerController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _repoMock = new Mock<IWatchtowerRepository>();
        _hubMock = new Mock<IHubContext<ActivityHub>>();

        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        _hubMock.Setup(h => h.Clients).Returns(mockClients.Object);

        _controller = new WatchtowerController(_repoMock.Object, _hubMock.Object);
    }

    [TestMethod]
    public async Task Webhook_StoresEventAndReturnsOk()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            """{"title":"Watchtower","message":"- trading-pack (ghcr.io/hound-ai/trading-pack:latest): abc1234 updated to def5678"}""");

        var result = await _controller.Webhook(payload, CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(r => r.StoreEventAsync(It.IsAny<WatchtowerEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Webhook_ParsesContainerAndImageFromMessage()
    {
        WatchtowerEvent? stored = null;
        _repoMock.Setup(r => r.StoreEventAsync(It.IsAny<WatchtowerEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WatchtowerEvent, CancellationToken>((e, _) => stored = e);

        var payload = JsonSerializer.Deserialize<JsonElement>(
            """{"title":"Update","message":"- trading-pack (ghcr.io/hound-ai/trading-pack:latest): abc1234 updated to def5678"}""");

        await _controller.Webhook(payload, CancellationToken.None);

        Assert.IsNotNull(stored);
        Assert.AreEqual("trading-pack", stored!.ContainerName);
        Assert.AreEqual("ghcr.io/hound-ai/trading-pack:latest", stored.ImageName);
        Assert.AreEqual("abc1234", stored.OldImageId);
        Assert.AreEqual("def5678", stored.NewImageId);
        Assert.AreEqual("Update", stored.Action);
    }

    [TestMethod]
    public async Task Webhook_BroadcastsOnWatchtowerEvent()
    {
        var mockClientProxy = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubClients>();
        mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
        _hubMock.Setup(h => h.Clients).Returns(mockClients.Object);

        var payload = JsonSerializer.Deserialize<JsonElement>(
            """{"title":"Watchtower","message":"test"}""");

        await _controller.Webhook(payload, CancellationToken.None);

        mockClientProxy.Verify(
            c => c.SendCoreAsync("OnWatchtowerEvent", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetEvents_ReturnsEventsFromRepository()
    {
        var events = new List<WatchtowerEvent>
        {
            new() { Id = "1", ContainerName = "trading-pack", Action = "updated" }
        };
        _repoMock.Setup(r => r.GetEventsAsync(1, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        var result = await _controller.GetEvents(cancellationToken: CancellationToken.None);

        var okResult = result.Result as OkObjectResult;
        Assert.IsNotNull(okResult);
        var returned = okResult!.Value as IReadOnlyList<WatchtowerEvent>;
        Assert.AreEqual(1, returned!.Count);
    }

    [TestMethod]
    public async Task Webhook_HandlesEmptyPayloadGracefully()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("{}");

        var result = await _controller.Webhook(payload, CancellationToken.None);

        Assert.IsInstanceOfType<OkResult>(result);
        _repoMock.Verify(r => r.StoreEventAsync(
            It.Is<WatchtowerEvent>(e => e.ContainerName == "" && e.Action == ""),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
