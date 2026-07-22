using Hound.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hound.Api.Controllers;

/// <summary>
/// Serves OHLCV bar data to the dashboard. The bars are sourced from the
/// trading pack via <see cref="IMarketDataClient"/> so the API container
/// itself doesn't need Alpaca credentials.
/// </summary>
[ApiController]
[Route("api/charts")]
public class ChartsController : ControllerBase
{
    private const string DefaultTimeframe = "1Day";
    private const int DefaultDays = 90;
    private const int MaxDays = 365 * 5;

    private static readonly HashSet<string> AllowedTimeframes = new(StringComparer.OrdinalIgnoreCase)
    {
        "1Min", "5Min", "15Min", "1Hour", "1Day", "1Week", "1Month",
    };

    private readonly IMarketDataClient _marketData;

    public ChartsController(IMarketDataClient marketData)
    {
        _marketData = marketData;
    }

    /// <summary>
    /// Returns OHLCV bars for <paramref name="symbol"/>. Accepts either a
    /// rolling window via <paramref name="days"/> or an explicit
    /// <paramref name="from"/>/<paramref name="to"/> pair.
    /// </summary>
    [HttpGet("{symbol}")]
    public async Task<IActionResult> GetBars(
        string symbol,
        [FromQuery] string timeframe = DefaultTimeframe,
        [FromQuery] int? days = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest(new { error = "symbol is required" });

        if (!AllowedTimeframes.Contains(timeframe))
            return BadRequest(new { error = $"Unsupported timeframe '{timeframe}'. Use one of: {string.Join(", ", AllowedTimeframes)}." });

        var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        DateTime fromUtc;
        if (from.HasValue)
        {
            fromUtc = from.Value.ToUniversalTime();
        }
        else
        {
            var windowDays = Math.Clamp(days ?? DefaultDays, 1, MaxDays);
            fromUtc = toUtc.AddDays(-windowDays);
        }

        if (fromUtc >= toUtc)
            return BadRequest(new { error = "'from' must be earlier than 'to'." });

        var response = await _marketData.GetBarsAsync(symbol, timeframe, fromUtc, toUtc, cancellationToken);
        if (response is null)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Failed to fetch bars",
                detail: "The trading pack did not return a usable response. Check trading-pack logs and Alpaca credentials.");
        }

        return Ok(response);
    }
}
