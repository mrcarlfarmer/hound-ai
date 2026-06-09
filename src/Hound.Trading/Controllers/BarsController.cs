using Alpaca.Markets;
using Hound.Trading.AlpacaClient;
using Microsoft.AspNetCore.Mvc;

namespace Hound.Trading.Controllers;

/// <summary>
/// Exposes historical OHLCV bars from the trading pack's Alpaca data client
/// over an intra-cluster HTTP endpoint. Consumed by <c>Hound.Api</c>'s
/// <c>ChartsController</c>, which proxies the data to the dashboard so the
/// API container never needs its own market-data credentials.
/// </summary>
[ApiController]
[Route("api/bars")]
public class BarsController : ControllerBase
{
    private readonly IAlpacaService _alpaca;
    private readonly ILogger<BarsController> _logger;

    public BarsController(IAlpacaService alpaca, ILogger<BarsController> logger)
    {
        _alpaca = alpaca;
        _logger = logger;
    }

    /// <summary>
    /// Returns historical bars for <paramref name="symbol"/> covering the
    /// supplied window. Defaults to the last 90 days of daily bars when
    /// <paramref name="from"/>/<paramref name="to"/> are omitted.
    /// </summary>
    /// <param name="symbol">Ticker (e.g. AAPL); coerced to upper-case.</param>
    /// <param name="timeframe">One of: 1Min, 5Min, 15Min, 1Hour, 1Day, 1Week, 1Month.</param>
    /// <param name="from">Start of the window (UTC). Defaults to <c>to - 90d</c>.</param>
    /// <param name="to">End of the window (UTC). Defaults to <c>UtcNow</c>.</param>
    [HttpGet("{symbol}")]
    public async Task<IActionResult> GetBars(
        string symbol,
        [FromQuery] string timeframe = "1Day",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest(new { error = "symbol is required" });

        if (!TryParseTimeFrame(timeframe, out var barTimeFrame))
            return BadRequest(new { error = $"Unsupported timeframe '{timeframe}'. Use 1Min, 5Min, 15Min, 1Hour, 1Day, 1Week, or 1Month." });

        var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        var fromUtc = (from ?? toUtc.AddDays(-90)).ToUniversalTime();

        if (fromUtc >= toUtc)
            return BadRequest(new { error = "'from' must be earlier than 'to'." });

        try
        {
            var bars = await _alpaca.GetBarsAsync(symbol.ToUpperInvariant(), fromUtc, toUtc, barTimeFrame, cancellationToken);
            var points = bars.Select(b => new
            {
                time = b.TimeUtc,
                open = b.Open,
                high = b.High,
                low = b.Low,
                close = b.Close,
                volume = b.Volume,
            });

            return Ok(new
            {
                symbol = symbol.ToUpperInvariant(),
                timeframe,
                from = fromUtc,
                to = toUtc,
                bars = points,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch bars for {Symbol}", symbol);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Failed to fetch bars",
                detail: ex.Message,
                type: "https://docs.alpaca.markets/reference/errors");
        }
    }

    private static bool TryParseTimeFrame(string timeframe, out BarTimeFrame result)
    {
        switch (timeframe?.Trim().ToLowerInvariant())
        {
            case "1min":
                result = new BarTimeFrame(1, BarTimeFrameUnit.Minute);
                return true;
            case "5min":
                result = new BarTimeFrame(5, BarTimeFrameUnit.Minute);
                return true;
            case "15min":
                result = new BarTimeFrame(15, BarTimeFrameUnit.Minute);
                return true;
            case "1hour":
                result = new BarTimeFrame(1, BarTimeFrameUnit.Hour);
                return true;
            case "1day":
                result = BarTimeFrame.Day;
                return true;
            case "1week":
                result = BarTimeFrame.Week;
                return true;
            case "1month":
                result = BarTimeFrame.Month;
                return true;
            default:
                result = default;
                return false;
        }
    }
}
