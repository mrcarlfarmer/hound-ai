namespace Hound.Api.Services;

/// <summary>
/// Periodically reconciles local <c>TradeDocument</c>s with Alpaca's authoritative
/// order state. Broadcasts <c>OnOrderUpdate</c> SignalR events for any drift.
/// </summary>
public class AlpacaSyncBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlpacaSyncBackgroundService> _logger;

    public AlpacaSyncBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AlpacaSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay so the API is fully warmed up before the first sync.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IAlpacaSyncService>();
                await syncService.SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic Alpaca sync failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
