using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.Hounds;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Trading.Services;

/// <summary>
/// Background service that runs TunerHound experiments on a configurable schedule.
/// Supports pause/resume via <see cref="Pause"/> and <see cref="Resume"/>.
/// </summary>
public class TunerHostedService : BackgroundService
{
    private readonly TunerHound _tunerHound;
    private readonly TunerSettings _settings;
    private readonly IActivityLogger _activityLogger;
    private readonly ILogger<TunerHostedService> _logger;

    private volatile bool _isPaused;

    public TunerHostedService(
        TunerHound tunerHound,
        IOptions<TunerSettings> settings,
        IActivityLogger activityLogger,
        ILogger<TunerHostedService> logger)
    {
        _tunerHound = tunerHound;
        _settings = settings.Value;
        _activityLogger = activityLogger;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the tuner is currently paused.</summary>
    public bool IsPaused => _isPaused;

    /// <summary>Pauses experiment execution after the current cycle completes.</summary>
    public void Pause()
    {
        _isPaused = true;
        _logger.LogInformation("TunerHostedService paused");
    }

    /// <summary>Resumes experiment execution.</summary>
    public void Resume()
    {
        _isPaused = false;
        _logger.LogInformation("TunerHostedService resumed");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _isPaused = _settings.StartPaused;

        _logger.LogInformation(
            "TunerHostedService starting. Interval: {Interval} minutes, StartPaused: {Paused}",
            _settings.RunIntervalMinutes, _settings.StartPaused);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromMinutes(_settings.RunIntervalMinutes);

            if (_isPaused)
            {
                _logger.LogDebug("TunerHostedService is paused; sleeping {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                continue;
            }

            try
            {
                _logger.LogInformation("TunerHostedService running experiment cycle at {Time}", DateTimeOffset.UtcNow);

                await _activityLogger.LogActivityAsync(new ActivityLog
                {
                    PackId = "trading-pack",
                    HoundId = "tuner-hound",
                    HoundName = "TunerHound",
                    Message = "Experiment cycle starting",
                    Severity = ActivitySeverity.Info,
                }, stoppingToken);

                var experiment = await _tunerHound.RunExperimentAsync(cancellationToken: stoppingToken);

                await _activityLogger.LogActivityAsync(new ActivityLog
                {
                    PackId = "trading-pack",
                    HoundId = "tuner-hound",
                    HoundName = "TunerHound",
                    Message = $"Experiment cycle complete: {experiment.HoundName} → {experiment.Status} (Δ={experiment.Delta:+0.000;-0.000;0.000})",
                    Severity = experiment.Status == TunerExperimentStatus.Crash
                        ? ActivitySeverity.Error
                        : ActivitySeverity.Success,
                    Metadata = new Dictionary<string, object>
                    {
                        ["experimentId"] = experiment.Id,
                        ["houndName"] = experiment.HoundName,
                        ["status"] = experiment.Status.ToString(),
                        ["delta"] = experiment.Delta,
                    },
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TunerHostedService encountered an error during experiment cycle");
            }

            _logger.LogInformation("TunerHostedService sleeping for {Delay}", delay);
            await Task.Delay(delay, stoppingToken);
        }

        _logger.LogInformation("TunerHostedService stopped");
    }
}

/// <summary>
/// Configuration for <see cref="TunerHostedService"/>.
/// </summary>
public class TunerSettings
{
    public const string SectionName = "Tuner";

    /// <summary>Minutes between experiment runs. Default: 30.</summary>
    public int RunIntervalMinutes { get; set; } = 30;

    /// <summary>When true the service starts in paused state and must be resumed via API.</summary>
    public bool StartPaused { get; set; } = false;

    /// <summary>Path to the hound config directory. Defaults to Config/ relative to the executable.</summary>
    public string? ConfigDirectory { get; set; }
}
