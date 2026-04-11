using Hound.Trading.Workflows;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Trading;

/// <summary>
/// Background service that runs the TradingWorkflow on a configurable schedule.
/// </summary>
public class TradingWorker : BackgroundService
{
    private readonly TradingWorkflow _workflow;
    private readonly TradingWorkflowSettings _settings;
    private readonly ILogger<TradingWorker> _logger;

    public TradingWorker(
        TradingWorkflow workflow,
        IOptions<TradingWorkflowSettings> settings,
        ILogger<TradingWorker> logger)
    {
        _workflow = workflow;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TradingWorker starting. Schedule: {Schedule}", _settings.CronSchedule);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("TradingWorker running workflow at {Time}", DateTimeOffset.UtcNow);
                await _workflow.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TradingWorker encountered an error during workflow run");
            }

            // Wait for the configured interval (default 4 hours on weekdays).
            // A full cron scheduler is outside scope; using a fixed interval as a placeholder.
            var delay = TimeSpan.FromHours(4);
            _logger.LogInformation("TradingWorker sleeping for {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }

        _logger.LogInformation("TradingWorker stopped");
    }
}
