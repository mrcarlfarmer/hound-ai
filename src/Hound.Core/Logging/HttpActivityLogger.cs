using System.Net.Http.Json;
using Hound.Core.Models;

namespace Hound.Core.Logging;

/// <summary>
/// <see cref="IActivityLogger"/> that forwards activity events to the Hound.Api over HTTP.
/// The API endpoint persists the event in RavenDB and broadcasts it to SignalR clients,
/// hooking pack containers into the real-time eventing framework.
/// </summary>
public class HttpActivityLogger : IActivityLogger
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;

    public HttpActivityLogger(IHttpClientFactory httpClientFactory, string baseUrl)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task LogActivityAsync(ActivityLog activity, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync($"{_baseUrl}/api/activity", activity, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ActivityLog>> GetActivitiesAsync(
        string? packId = null,
        string? houndId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();

        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(packId))
            queryParams.Add($"pack={Uri.EscapeDataString(packId)}");
        if (!string.IsNullOrWhiteSpace(houndId))
            queryParams.Add($"hound={Uri.EscapeDataString(houndId)}");
        if (from.HasValue)
            queryParams.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to.HasValue)
            queryParams.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        queryParams.Add($"page={page}");
        queryParams.Add($"pageSize={pageSize}");

        var url = $"{_baseUrl}/api/activity?{string.Join("&", queryParams)}";
        var results = await client.GetFromJsonAsync<List<ActivityLog>>(url, cancellationToken);
        return (results ?? []).AsReadOnly();
    }
}
