using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery;
using Hound.Grocery.Graph;
using Hound.Grocery.Nodes;
using Moq;

namespace Hound.Grocery.Tests.Nodes;

[TestClass]
public class ConciergeHoundTests
{
    private Mock<IActivityLogger> _mockLogger = null!;
    private ConciergeHound _hound = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<IActivityLogger>();
        _hound = new ConciergeHound(_mockLogger.Object);
    }

    [TestMethod]
    public void NodeId_And_PackId_FollowConventions()
    {
        Assert.AreEqual("concierge-hound", _hound.NodeId);
        Assert.AreEqual(GroceryPack.PackId, _hound.PackId);
        Assert.AreEqual("grocery-pack", _hound.PackId);
    }

    [TestMethod]
    public async Task ExecuteAsync_LogsActivity_AndAdvancesState()
    {
        var state = GroceryGraphState.Initial();

        var result = await _hound.ExecuteAsync(state, default);

        Assert.AreEqual(_hound.NodeId, result.CurrentNode);
        _mockLogger.Verify(
            l => l.LogActivityAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
