using Alpaca.Markets;
using Hound.Core.Models;
using Hound.Trading.Services;

namespace Hound.Trading.Tests.Services;

[TestClass]
public sealed class OrderWatcherServiceTests
{
    [TestMethod]
    public void MapOrderStatus_New_ReturnsPending()
    {
        Assert.AreEqual(FillStatus.Pending, OrderWatcherService.MapOrderStatus(OrderStatus.New));
    }

    [TestMethod]
    public void MapOrderStatus_Accepted_ReturnsPending()
    {
        Assert.AreEqual(FillStatus.Pending, OrderWatcherService.MapOrderStatus(OrderStatus.Accepted));
    }

    [TestMethod]
    public void MapOrderStatus_PendingNew_ReturnsPending()
    {
        Assert.AreEqual(FillStatus.Pending, OrderWatcherService.MapOrderStatus(OrderStatus.PendingNew));
    }

    [TestMethod]
    public void MapOrderStatus_PartiallyFilled_ReturnsPartiallyFilled()
    {
        Assert.AreEqual(FillStatus.PartiallyFilled, OrderWatcherService.MapOrderStatus(OrderStatus.PartiallyFilled));
    }

    [TestMethod]
    public void MapOrderStatus_Filled_ReturnsFilled()
    {
        Assert.AreEqual(FillStatus.Filled, OrderWatcherService.MapOrderStatus(OrderStatus.Filled));
    }

    [TestMethod]
    public void MapOrderStatus_Canceled_ReturnsCanceled()
    {
        Assert.AreEqual(FillStatus.Canceled, OrderWatcherService.MapOrderStatus(OrderStatus.Canceled));
    }

    [TestMethod]
    public void MapOrderStatus_Expired_ReturnsExpired()
    {
        Assert.AreEqual(FillStatus.Expired, OrderWatcherService.MapOrderStatus(OrderStatus.Expired));
    }

    [TestMethod]
    public void MapOrderStatus_Rejected_ReturnsRejected()
    {
        Assert.AreEqual(FillStatus.Rejected, OrderWatcherService.MapOrderStatus(OrderStatus.Rejected));
    }
}
