using System.Net.Http.Json;
using System.Text.Json;
using Hound.Core.Models;
using Raven.Client.Documents;

namespace Hound.Api.Services;

public class HealthCheckService : IHealthCheckService
{
    private readonly IDocumentStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HealthCheckService> _logger;

    public HealthCheckService(
        IDocumentStore store,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HealthCheckService> logger)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthReport> CheckAllAsync(CancellationToken cancellationToken = default)
    {
        var checks = await Task.WhenAll(
            CheckRavenDbAsync(cancellationToken),
            CheckOllamaAsync(cancellationToken),
            CheckTradingPackAsync(cancellationToken));

        var apiHealth = new ServiceHealth { Name = "hound-api", Status = HealthStatus.Healthy };

        var services = new List<ServiceHealth> { apiHealth };
        services.AddRange(checks);

        var worstStatus = services.Max(s => s.Status);

        return new HealthReport
        {
            Status = worstStatus,
            Timestamp = DateTime.UtcNow,
            Services = services
        };
    }

    private async Task<ServiceHealth> CheckRavenDbAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var session = _store.OpenAsyncSession();
            await session.Query<ActivityLog>().Take(1).ToListAsync(cancellationToken);
            return new ServiceHealth { Name = "ravendb", Status = HealthStatus.Healthy };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RavenDB health check failed");
            return new ServiceHealth { Name = "ravendb", Status = HealthStatus.Unhealthy, Detail = ex.Message };
        }
    }

    private async Task<ServiceHealth> CheckOllamaAsync(CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Ollama:BaseUrl"]?.TrimEnd('/') ?? "http://ollama:11434/v1";
            var ollamaUrl = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? baseUrl[..^3]
                : baseUrl;

            var client = _httpClientFactory.CreateClient("health");
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{ollamaUrl}/api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var modelCount = 0;
            if (json.TryGetProperty("models", out var models))
                modelCount = models.GetArrayLength();

            return new ServiceHealth
            {
                Name = "ollama",
                Status = modelCount > 0 ? HealthStatus.Healthy : HealthStatus.Degraded,
                Detail = modelCount > 0 ? $"{modelCount} model{(modelCount == 1 ? "" : "s")} loaded" : "No models loaded"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama health check failed");
            return new ServiceHealth { Name = "ollama", Status = HealthStatus.Unhealthy, Detail = ex.Message };
        }
    }

    private async Task<ServiceHealth> CheckTradingPackAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var session = _store.OpenAsyncSession();
            var recent = await session.Query<ActivityLog>()
                .Where(a => a.PackId == "trading-pack")
                .OrderByDescending(a => a.Timestamp)
                .Take(1)
                .ToListAsync(cancellationToken);

            if (recent.Count == 0)
                return new ServiceHealth { Name = "trading-pack", Status = HealthStatus.Unknown, Detail = "No activity recorded" };

            var age = DateTime.UtcNow - recent[0].Timestamp;
            if (age < TimeSpan.FromMinutes(5))
                return new ServiceHealth { Name = "trading-pack", Status = HealthStatus.Healthy, Detail = $"Last activity {FormatAge(age)} ago" };

            return new ServiceHealth { Name = "trading-pack", Status = HealthStatus.Degraded, Detail = $"Last activity {FormatAge(age)} ago" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Trading pack health check failed");
            return new ServiceHealth { Name = "trading-pack", Status = HealthStatus.Unhealthy, Detail = ex.Message };
        }
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 60) return $"{(int)age.TotalSeconds}s";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours}h";
        return $"{(int)age.TotalDays}d";
    }
}
