using Alpaca.Markets;
using Hound.Trading.AlpacaClient;

namespace Hound.Eval;

/// <summary>
/// Stub <see cref="IAlpacaService"/> for use in the eval harness.
/// Returns minimal synthetic data so hounds can reason without live API access.
/// Symbols containing "NODATA" return empty bar lists to test insufficient-data paths.
/// </summary>
internal sealed class StubAlpacaService : IAlpacaService
{
    public Task<IAccount> GetAccountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IAccount>(new StubAccount());

    public Task<IReadOnlyList<IPosition>> ListPositionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IPosition>>(new List<IPosition>());

    public Task<IOrder> SubmitOrderAsync(
        string symbol,
        OrderQuantity quantity,
        OrderSide side,
        OrderType type,
        TimeInForce timeInForce,
        decimal? limitPrice = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IOrder>(new StubOrder(symbol, side, quantity, limitPrice));

    public Task<IReadOnlyList<IBar>> GetBarsAsync(
        string symbol,
        DateTime from,
        DateTime to,
        BarTimeFrame timeFrame,
        CancellationToken cancellationToken = default)
    {
        if (symbol.Contains("NODATA", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IReadOnlyList<IBar>>(new List<IBar>());

        var bars = new List<IBar>
        {
            new StubBar(symbol, from.AddDays(1), 148m, 152m, 146m, 150m, 1_100_000),
            new StubBar(symbol, from.AddDays(2), 150m, 155m, 149m, 154m, 1_250_000),
            new StubBar(symbol, from.AddDays(3), 154m, 159m, 153m, 158m, 1_050_000),
        };
        return Task.FromResult<IReadOnlyList<IBar>>(bars);
    }

    public Task<IOrder> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult<IOrder>(new StubOrder("STUB", OrderSide.Buy, OrderQuantity.Fractional(1m), null));

    public Task<IReadOnlyList<IOrder>> ListOrdersAsync(OrderStatusFilter? statusFilter = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IOrder>>(new List<IOrder>());

    public Task<bool> CancelOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

internal sealed class StubAccount : IAccount
{
    public Guid AccountId => Guid.Empty;
    public string AccountNumber => "EVAL_TEST_001";
    public decimal? AccruedFees => null;
    public decimal? BuyingPower => 50_000m;
    public DateTime CreatedAtUtc => new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public AccountStatus CryptoStatus => AccountStatus.Active;
    public string Currency => "USD";
    public ulong DayTradeCount => 0;
    public decimal? DayTradingBuyingPower => null;
    public decimal? Equity => 100_000m;
    public decimal? InitialMargin => null;
    public bool IsAccountBlocked => false;
    public bool IsDayPatternTrader => false;
    public bool IsTradingBlocked => false;
    public bool IsTransfersBlocked => false;
    public decimal LastEquity => 99_000m;
    public decimal LastMaintenanceMargin => 0m;
    public decimal? LongMarketValue => null;
    public decimal MaintenanceMargin => 0m;
    public Multiplier Multiplier => Multiplier.Double;
    public decimal? NonMarginableBuyingPower => null;
    public OptionsTradingLevel? OptionsApprovedLevel => null;
    public decimal? OptionsBuyingPower => null;
    public OptionsTradingLevel? OptionsTradingLevel => null;
    public decimal? PendingTransferIn => null;
    public decimal? PendingTransferOut => null;
    public decimal? RegulationBuyingPower => null;
    public bool ShortingEnabled => false;
    public decimal? ShortMarketValue => null;
    public decimal Sma => 0m;
    public AccountStatus Status => AccountStatus.Active;
    public decimal TradableCash => 50_000m;
    public bool TradeSuspendedByUser => false;
}

internal sealed class StubBar : IBar
{
    public StubBar(string symbol, DateTime timeUtc, decimal open, decimal high, decimal low, decimal close, decimal volume)
    {
        Symbol = symbol;
        TimeUtc = timeUtc;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }

    public string Symbol { get; }
    public DateTime TimeUtc { get; }
    public decimal Open { get; }
    public decimal High { get; }
    public decimal Low { get; }
    public decimal Close { get; }
    public decimal Volume { get; }
    public ulong TradeCount => 0;
    public decimal Vwap => (Open + Close) / 2m;
}

internal sealed class StubOrder : IOrder
{
    public StubOrder(string symbol, OrderSide side, OrderQuantity quantity, decimal? limitPrice)
    {
        Symbol = symbol;
        OrderSide = side;
        LimitPrice = limitPrice;
        Quantity = (decimal?)quantity.Value;
        OrderId = Guid.NewGuid();
        OrderType = limitPrice.HasValue ? OrderType.Limit : OrderType.Market;
    }

    public Guid OrderId { get; }
    public OrderSide OrderSide { get; }
    public OrderStatus OrderStatus => OrderStatus.New;
    public string Symbol { get; }
    public decimal? Quantity { get; }
    public decimal FilledQuantity => 0m;
    public long IntegerFilledQuantity => 0;
    public long IntegerQuantity => (long)(Quantity ?? 0);
    public IReadOnlyList<IOrder> Legs => Array.Empty<IOrder>();
    public decimal? LimitPrice { get; }
    public decimal? Notional => null;
    public OrderClass OrderClass => OrderClass.Simple;
    public OrderType OrderType { get; }
    public TimeInForce TimeInForce => TimeInForce.Day;
    public AssetClass AssetClass => AssetClass.UsEquity;
    public Guid AssetId => Guid.Empty;
    public decimal? AverageFillPrice => null;
    public DateTime? CancelledAtUtc => null;
    public string ClientOrderId => string.Empty;
    public DateTime? CreatedAtUtc => DateTime.UtcNow;
    public DateTime? ExpiredAtUtc => null;
    public DateTime? FailedAtUtc => null;
    public DateTime? FilledAtUtc => null;
    public decimal? HighWaterMark => null;
    public Guid? ReplacedByOrderId => null;
    public Guid? ReplacesOrderId => null;
    public decimal? StopPrice => null;
    public DateTime? SubmittedAtUtc => DateTime.UtcNow;
    public decimal? TrailOffsetInDollars => null;
    public decimal? TrailOffsetInPercent => null;
    public DateTime? UpdatedAtUtc => null;
    public DateTime? ReplacedAtUtc => null;
}
