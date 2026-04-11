using Alpaca.Markets;
using Hound.Trading.AlpacaClient;
using Microsoft.Extensions.Options;

namespace Hound.Trading.Tests.AlpacaClient;

[TestClass]
public sealed class AlpacaServiceTests
{
    private AlpacaService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new AlpacaService(Options.Create(new AlpacaSettings()));
    }

    [TestMethod]
    public void AlpacaService_CanBeConstructed()
    {
        var service = new AlpacaService(Options.Create(new AlpacaSettings()));
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void AlpacaService_ImplementsInterface()
    {
        Assert.IsInstanceOfType<IAlpacaService>(_service);
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task GetAccountAsync_RequiresCredentials()
    {
        await _service.GetAccountAsync();
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task GetAccountAsync_WithCancellationToken_RequiresCredentials()
    {
        using var cts = new CancellationTokenSource();
        await _service.GetAccountAsync(cts.Token);
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task ListPositionsAsync_RequiresCredentials()
    {
        await _service.ListPositionsAsync();
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task SubmitOrderAsync_RequiresCredentials()
    {
        await _service.SubmitOrderAsync(
            symbol: "AAPL",
            quantity: OrderQuantity.Fractional(1m),
            side: OrderSide.Buy,
            type: OrderType.Market,
            timeInForce: TimeInForce.Day);
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task SubmitOrderAsync_WithLimitPrice_RequiresCredentials()
    {
        await _service.SubmitOrderAsync(
            symbol: "TSLA",
            quantity: OrderQuantity.Fractional(2m),
            side: OrderSide.Sell,
            type: OrderType.Limit,
            timeInForce: TimeInForce.Gtc,
            limitPrice: 250.00m);
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task GetBarsAsync_RequiresCredentials()
    {
        var from = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        await _service.GetBarsAsync(
            symbol: "AAPL",
            from: from,
            to: to,
            timeFrame: BarTimeFrame.Day);
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task GetBarsAsync_WithHourlyFrame_RequiresCredentials()
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;

        await _service.GetBarsAsync(
            symbol: "MSFT",
            from: from,
            to: to,
            timeFrame: BarTimeFrame.Hour);
    }
}
