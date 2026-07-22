using Alpaca.Markets;
using Hound.Trading.AlpacaClient;
using Microsoft.Extensions.Options;
using Moq;

namespace Hound.Trading.Tests.AlpacaClient;

[TestClass]
public sealed class AlpacaServiceTests
{
    private static IOptions<AlpacaSettings> CreateTestOptions() =>
        Options.Create(new AlpacaSettings
        {
            ApiKeyId = "test-key-id",
            SecretKey = "test-secret-key",
            Environment = "Paper"
        });

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

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task GetOrderAsync_RequiresCredentials()
    {
        await _service.GetOrderAsync(Guid.NewGuid());
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task ListOrdersAsync_RequiresCredentials()
    {
        await _service.ListOrdersAsync();
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task ListOrdersAsync_WithFilter_RequiresCredentials()
    {
        await _service.ListOrdersAsync(OrderStatusFilter.Open);
    }

    [TestMethod]
    [Ignore("Requires live Alpaca API credentials and network access")]
    public async Task CancelOrderAsync_RequiresCredentials()
    {
        await _service.CancelOrderAsync(Guid.NewGuid());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Defensive guards on SubmitTrailingStopOrderAsync — these are pure
    // input-validation paths that fail BEFORE we touch the broker, so they
    // can run without live credentials and don't need the Ignore attribute.
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SubmitTrailingStopOrderAsync_FractionalQuantity_Throws()
    {
        // Alpaca categorically rejects stop / trailing-stop orders on
        // fractional positions; the SDK only finds out at broker round-trip
        // time and the error message ("fractional orders must be DAY
        // orders") is misleading. The service must short-circuit and tell
        // the caller exactly why.
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _service.SubmitTrailingStopOrderAsync(
                symbol: "GOOG",
                quantity: OrderQuantity.Fractional(0.3315m),
                side: OrderSide.Sell,
                trailPercent: 5m,
                timeInForce: TimeInForce.Gtc));

        StringAssert.Contains(ex.Message, "fractional");
    }

    [TestMethod]
    public async Task SubmitTrailingStopOrderAsync_NonPositiveTrailPercent_Throws()
    {
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(
            () => _service.SubmitTrailingStopOrderAsync(
                symbol: "AAPL",
                quantity: OrderQuantity.Fractional(5m),
                side: OrderSide.Sell,
                trailPercent: 0m,
                timeInForce: TimeInForce.Gtc));
    }
}
