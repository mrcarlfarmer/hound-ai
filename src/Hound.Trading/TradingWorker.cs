using Hound.Core.Models;
using Hound.Trading.Graph;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using System.Net.Http.Json;

namespace Hound.Trading;

/// <summary>
/// Background service that runs the TradingGraph on a configurable schedule
/// and processes on-demand run requests from the dashboard.
/// </summary>
public class TradingWorker : BackgroundService
{
    private const string PackId = "trading-pack";
    private const string Database = "hound-trading-pack";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly TradingGraph _graph;
    private readonly TradingGraphSettings _settings;
    private readonly IDocumentStore _documentStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiBaseUrl;
    private readonly ILogger<TradingWorker> _logger;

    public TradingWorker(
        TradingGraph graph,
        IOptions<TradingGraphSettings> settings,
        IDocumentStore documentStore,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TradingWorker> logger)
    {
        _graph = graph;
        _settings = settings.Value;
        _documentStore = documentStore;
        _httpClientFactory = httpClientFactory;
        _apiBaseUrl = (configuration["HoundApi:BaseUrl"] ?? "http://hound-api:8080").TrimEnd('/');
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RegisterPackAsync(stoppingToken);

        _logger.LogInformation("TradingWorker starting. Poll interval: {Poll}s, scheduled interval: {Interval} min",
            PollInterval.TotalSeconds, _settings.RunIntervalMinutes);

        var nextScheduledRun = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Process any pending on-demand requests
                await ProcessPendingRequestsAsync(stoppingToken);

                // Run scheduled symbols if due
                if (_settings.Symbols.Count > 0 && DateTimeOffset.UtcNow >= nextScheduledRun)
                {
                    foreach (var symbol in _settings.Symbols)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        await _graph.RunAsync(symbol, stoppingToken);
                    }
                    nextScheduledRun = DateTimeOffset.UtcNow.AddMinutes(_settings.RunIntervalMinutes);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TradingWorker encountered an error");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("TradingWorker stopped");
    }

    private async Task ProcessPendingRequestsAsync(CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(Database);
        var pending = await session.Query<RunRequest>()
            .Where(r => r.Status == RunRequestStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        foreach (var request in pending)
        {
            if (cancellationToken.IsCancellationRequested) break;

            _logger.LogInformation("Processing run request {Id} for {Symbol}", request.Id, request.Symbol);

            request.Status = RunRequestStatus.Running;
            request.StartedAt = DateTime.UtcNow;
            await session.SaveChangesAsync(cancellationToken);

            try
            {
                var state = await _graph.RunAsync(request.Symbol, cancellationToken);
                request.Status = state.ErrorMessage is not null
                    ? RunRequestStatus.Failed
                    : RunRequestStatus.Completed;
                request.RunId = state.RunId;
                request.ErrorMessage = state.ErrorMessage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run request {Id} failed", request.Id);
                request.Status = RunRequestStatus.Failed;
                request.ErrorMessage = ex.Message;
            }

            request.CompletedAt = DateTime.UtcNow;
            await session.SaveChangesAsync(cancellationToken);
        }
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
