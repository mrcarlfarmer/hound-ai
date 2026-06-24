using Hound.Core.Models;
using Hound.Grocery.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace Hound.Grocery;

/// <summary>
/// Background service that registers the Grocery Pack with <c>hound-api</c> on
/// startup and bootstraps the markdown state directory.
/// <para>
/// <b>Phase 1 scaffold:</b> the worker only registers the pack and idles. The
/// Telegram long-poll intake loop (Phase 2) and the scheduled basket-build
/// trigger (Phase 4+) are not wired up yet.
/// </para>
/// </summary>
public class GroceryWorker : BackgroundService
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

    private readonly StateFileService _stateFiles;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiBaseUrl;
    private readonly ILogger<GroceryWorker> _logger;

    public GroceryWorker(
        StateFileService stateFiles,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GroceryWorker> logger)
    {
        _stateFiles = stateFiles;
        _httpClientFactory = httpClientFactory;
        _apiBaseUrl = (configuration["HoundApi:BaseUrl"] ?? "http://hound-api:8080").TrimEnd('/');
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TryEnsureStateDirectory();
        await RegisterPackAsync(stoppingToken);

        _logger.LogInformation("GroceryWorker started (Phase 1 scaffold — idle).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(IdleInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("GroceryWorker stopped.");
    }

    private void TryEnsureStateDirectory()
    {
        try
        {
            _stateFiles.EnsureDirectory();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not create grocery state directory; continuing.");
        }
    }

    private async Task RegisterPackAsync(CancellationToken cancellationToken)
    {
        var registration = new PackRegistration
        {
            Id = GroceryPack.PackId,
            Name = GroceryPack.DisplayName,
            Hounds =
            [
                new() { Id = "concierge-hound", Name = "ConciergeHound" },
                new() { Id = "planner-hound", Name = "PlannerHound" },
                new() { Id = "shopper-hound", Name = "ShopperHound" },
                new() { Id = "budget-hound", Name = "BudgetHound" },
                new() { Id = "learner-hound", Name = "LearnerHound" },
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
                _logger.LogInformation("Pack '{PackId}' registered with API", GroceryPack.PackId);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Pack registration attempt {Attempt}/10 failed, retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), cancellationToken);
            }
        }

        _logger.LogError("Failed to register pack '{PackId}' after 10 attempts", GroceryPack.PackId);
    }
}
