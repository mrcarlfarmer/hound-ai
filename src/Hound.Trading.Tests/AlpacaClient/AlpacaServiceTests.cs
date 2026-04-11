using Alpaca.Markets;
using Hound.Trading.AlpacaClient;

namespace Hound.Trading.Tests.AlpacaClient;

[TestClass]
public sealed class AlpacaServiceTests
{
    private AlpacaService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new AlpacaService();
    }

    [TestMethod]
    public void AlpacaService_CanBeConstructed()
    {
        var service = new AlpacaService();
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void AlpacaService_ImplementsInterface()
    {
        Assert.IsInstanceOfType<IAlpacaService>(_service);
    }

    [TestMethod]
    public async Task GetAccountAsync_NotYetImplemented_ThrowsNotImplementedException()
    {
        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _service.GetAccountAsync());
    }

    [TestMethod]
    public async Task GetAccountAsync_WithCancellationToken_ThrowsNotImplementedException()
    {
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _service.GetAccountAsync(cts.Token));
    }

    [TestMethod]
    public async Task ListPositionsAsync_NotYetImplemented_ThrowsNotImplementedException()
    {
        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _service.ListPositionsAsync());
    }

    [TestMethod]
    public async Task SubmitOrderAsync_NotYetImplemented_ThrowsNotImplementedException()
    {
        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _service.SubmitOrderAsync(
                symbol: "AAPL",
                quantity: OrderQuantity.Fractional(1m),
                side: OrderSide.Buy,
                type: OrderType.Market,
                timeInForce: TimeInForce.Day));
    }

    [TestMethod]
    public async Task SubmitOrderAsync_WithLimitPrice_ThrowsNotImplementedException()
    {
        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _service.SubmitOrderAsync(
                symbol: "TSLA",
                quantity: OrderQuantity.Fractional(2m),
                side: OrderSide.Sell,
                type: OrderType.Limit,
                timeInForce: TimeInForce.Gtc,
                limitPrice: 250.00m));
    }

    [TestMethod]
    public async Task GetBarsAsync_NotYetImplemented_ThrowsNotImplementedException()
    {
        var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _service.GetBarsAsync(
                symbol: "AAPL",
                from: from,
                to: to,
                timeFrame: BarTimeFrame.Day));
    }

    [TestMethod]
    public async Task GetBarsAsync_WithHourlyFrame_ThrowsNotImplementedException()
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;

        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _service.GetBarsAsync(
                symbol: "MSFT",
                from: from,
                to: to,
                timeFrame: BarTimeFrame.Hour));
    }
}
