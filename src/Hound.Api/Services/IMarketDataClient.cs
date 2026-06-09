using System.Net.Http.Json;
using System.Text.Json;

namespace Hound.Api.Services;

/// <summary>
/// Read-only market data accessor that talks to the trading pack's HTTP
/// surface. Keeping this in the API layer means Hound.Api does not need to
/// hold Alpaca market-data credentials &mdash; all access is proxied through
/// the pack that already owns those secrets.
/// </summary>
public interface IMarketDataClient
{
    /// <summary>
    /// Fetches OHLCV bars for <paramref name="symbol"/> over the supplied
    /// window. Returns <c>null</c> when the upstream call fails &mdash; the
    /// caller should surface a 502 / empty chart rather than throwing.
    /// </summary>
    Task<BarsResponse?> GetBarsAsync(
        string symbol,
        string timeframe,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}

public sealed record BarsResponse(
    string Symbol,
    string Timeframe,
    DateTime From,
    DateTime To,
    IReadOnlyList<BarPoint> Bars);

public sealed record BarPoint(
    DateTime Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

public sealed class HttpMarketDataClient : IMarketDataClient
{
    private const string HttpClientName = "trading-pack";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly ILogger<HttpMarketDataClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public HttpMarketDataClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HttpMarketDataClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = (configuration["TradingPack:BaseUrl"] ?? "http://trading-pack:8080").TrimEnd('/');
        _logger = logger;
    }

    public async Task<BarsResponse?> GetBarsAsync(
        string symbol,
        string timeframe,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(10);

        var fromIso = from.ToUniversalTime().ToString("O");
        var toIso = to.ToUniversalTime().ToString("O");
        var url = $"{_baseUrl}/api/bars/{Uri.EscapeDataString(symbol)}"
                  + $"?timeframe={Uri.EscapeDataString(timeframe)}"
                  + $"&from={Uri.EscapeDataString(fromIso)}"
                  + $"&to={Uri.EscapeDataString(toIso)}";

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Trading pack returned {Status} for bars {Symbol} {Timeframe}: {Body}",
                    (int)response.StatusCode, symbol, timeframe, body);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<BarsResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch bars from trading pack for {Symbol} {Timeframe}", symbol, timeframe);
            return null;
        }
    }
}
