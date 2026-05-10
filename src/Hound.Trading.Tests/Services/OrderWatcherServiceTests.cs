using Alpaca.Markets;
using Hound.Core.Models;
using Hound.Trading.Nodes;

namespace Hound.Trading.Tests.Nodes;

[TestClass]
public sealed class MonitorNodeStatusMappingTests
{
    [TestMethod]
    public void MapOrderStatus_New_ReturnsPending()
    {
        Assert.AreEqual(FillStatus.Pending, MonitorNode.MapOrderStatus(OrderStatus.New));
    }

    [TestMethod]
    public void MapOrderStatus_Accepted_ReturnsPending()
    {
        Assert.AreEqual(FillStatus.Pending, MonitorNode.MapOrderStatus(OrderStatus.Accepted));
    }

    [TestMethod]
    public void MapOrderStatus_PendingNew_ReturnsPending()
    {
        Assert.AreEqual(FillStatus.Pending, MonitorNode.MapOrderStatus(OrderStatus.PendingNew));
    }

    [TestMethod]
    public void MapOrderStatus_PartiallyFilled_ReturnsPartiallyFilled()
    {
        Assert.AreEqual(FillStatus.PartiallyFilled, MonitorNode.MapOrderStatus(OrderStatus.PartiallyFilled));
    }

    [TestMethod]
    public void MapOrderStatus_Filled_ReturnsFilled()
    {
        Assert.AreEqual(FillStatus.Filled, MonitorNode.MapOrderStatus(OrderStatus.Filled));
    }

    [TestMethod]
    public void MapOrderStatus_Canceled_ReturnsCanceled()
    {
        Assert.AreEqual(FillStatus.Canceled, MonitorNode.MapOrderStatus(OrderStatus.Canceled));
    }

    [TestMethod]
    public void MapOrderStatus_Expired_ReturnsExpired()
    {
        Assert.AreEqual(FillStatus.Expired, MonitorNode.MapOrderStatus(OrderStatus.Expired));
    }

    [TestMethod]
    public void MapOrderStatus_Rejected_ReturnsRejected()
    {
        Assert.AreEqual(FillStatus.Rejected, MonitorNode.MapOrderStatus(OrderStatus.Rejected));
    }
}
