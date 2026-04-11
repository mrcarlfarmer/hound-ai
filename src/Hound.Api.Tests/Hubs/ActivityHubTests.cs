using Hound.Api.Hubs;
using Hound.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Hound.Api.Tests.Hubs;

[TestClass]
public class ActivityHubTests
{
    [TestMethod]
    public async Task SubscribeToPack_AddsConnectionToPackGroup()
    {
        var mockGroups = new Mock<IGroupManager>();
        mockGroups
            .Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(c => c.ConnectionId).Returns("conn-abc");

        var hub = new ActivityHub
        {
            Groups = mockGroups.Object,
            Context = mockContext.Object
        };

        await hub.SubscribeToPack("trading");

        mockGroups.Verify(
            g => g.AddToGroupAsync("conn-abc", "pack-trading", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task UnsubscribeFromPack_RemovesConnectionFromPackGroup()
    {
        var mockGroups = new Mock<IGroupManager>();
        mockGroups
            .Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(c => c.ConnectionId).Returns("conn-abc");

        var hub = new ActivityHub
        {
            Groups = mockGroups.Object,
            Context = mockContext.Object
        };

        await hub.UnsubscribeFromPack("trading");

        mockGroups.Verify(
            g => g.RemoveFromGroupAsync("conn-abc", "pack-trading", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task PublishActivity_SendsOnActivityToPackGroup()
    {
        var mockClientProxy = new Mock<IClientProxy>();
        mockClientProxy
            .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockClients = new Mock<IHubCallerClients>();
        mockClients.Setup(c => c.Group("pack-trading")).Returns(mockClientProxy.Object);

        var hub = new ActivityHub
        {
            Clients = mockClients.Object
        };

        var activity = new ActivityLog
        {
            Id = "logs/1",
            PackId = "trading",
            HoundName = "AnalysisHound",
            Message = "Buy signal detected"
        };

        await hub.PublishActivity(activity);

        mockClientProxy.Verify(
            p => p.SendCoreAsync(
                "OnActivity",
                It.Is<object?[]>(args => args.Length == 1 && args[0] == activity),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task SubscribeToPack_UsesPrefixedGroupName()
    {
        var mockGroups = new Mock<IGroupManager>();
        mockGroups
            .Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(c => c.ConnectionId).Returns("conn-xyz");

        var hub = new ActivityHub
        {
            Groups = mockGroups.Object,
            Context = mockContext.Object
        };

        await hub.SubscribeToPack("risk-pack");

        mockGroups.Verify(
            g => g.AddToGroupAsync("conn-xyz", "pack-risk-pack", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
