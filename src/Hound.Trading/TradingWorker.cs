using Hound.Core.Models;
using Hound.Trading.Graph;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Hound.Trading;

/// <summary>
/// Background service that runs the TradingGraph on a configurable schedule.
/// </summary>
public class TradingWorker : BackgroundService
{
    private const string PackId = "trading-pack";

    private readonly TradingGraph _graph;
    private readonly TradingGraphSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiBaseUrl;
    private readonly ILogger<TradingWorker> _logger;

    public TradingWorker(
        TradingGraph graph,
        IOptions<TradingGraphSettings> settings,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TradingWorker> logger)
    {
        _graph = graph;
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _apiBaseUrl = (configuration["HoundApi:BaseUrl"] ?? "http://hound-api:8080").TrimEnd('/');
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RegisterPackAsync(stoppingToken);

        _logger.LogInformation("TradingWorker starting. Interval: {Interval} minutes", _settings.RunIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("TradingWorker running graph at {Time}", DateTimeOffset.UtcNow);

                foreach (var symbol in _settings.Symbols)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await _graph.RunAsync(symbol, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TradingWorker encountered an error during graph run");
            }

            var delay = TimeSpan.FromMinutes(_settings.RunIntervalMinutes);
            _logger.LogInformation("TradingWorker sleeping for {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }

        _logger.LogInformation("TradingWorker stopped");
    }

    private async Task RegisterPackAsync(CancellationToken cancellationToken)
    {
        var registration = new PackRegistration
        {
            Id = PackId,
            Name = "Trading Pack",
            Hounds =
            [
                new() { Id = "data-node", Name = "DataNode" },
                new() { Id = "strategy-node", Name = "StrategyNode" },
                new() { Id = "risk-node", Name = "RiskNode" },
                new() { Id = "execution-node", Name = "ExecutionNode" },
                new() { Id = "monitor-node", Name = "MonitorNode" },
            ]
        };

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.PostAsJsonAsync(
                    $"{_apiBaseUrl}/api/packs/register", registration, cancellationToken);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("Pack '{PackId}' registered with API", PackId);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Pack registration attempt {Attempt}/10 failed, retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), cancellationToken);
            }
        }

        _logger.LogError("Failed to register pack '{PackId}' after 10 attempts", PackId);
    }
}
