using System.Net.Http.Json;
using Hound.Grocery.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Services;

/// <summary>
/// HTTP implementation of <see cref="IBrowserWorkerClient"/> that calls the
/// <c>grocery-browser</c> sidecar over <c>hound-net</c>.
/// <para>
/// <b>Phase 1 scaffold:</b> the methods are wired to the sidecar's stubbed
/// endpoints but are not yet invoked by any node. Real ranking/substitution
/// logic arrives in Phase 4.
/// </para>
/// </summary>
public class BrowserWorkerClient : IBrowserWorkerClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly ILogger<BrowserWorkerClient>? _logger;

    public BrowserWorkerClient(
        IHttpClientFactory httpClientFactory,
        IOptions<BrowserWorkerSettings> options,
        ILoggerFactory? loggerFactory = null)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = options.Value.BaseUrl.TrimEnd('/');
        _logger = loggerFactory?.CreateLogger<BrowserWorkerClient>();
    }

    public async Task<LoginResult> LoginAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsync($"{_baseUrl}/login", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResult>(cancellationToken)
            ?? new LoginResult(false, "empty response");
    }

    public async Task<SearchResult> SearchAsync(string term, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync($"{_baseUrl}/search", new SearchRequest(term), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SearchResult>(cancellationToken)
            ?? new SearchResult(term, Array.Empty<ProductCandidate>());
    }

    public async Task<BasketSnapshot> AddToBasketAsync(string productId, double quantity, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"{_baseUrl}/add", new AddToBasketRequest(productId, quantity), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BasketSnapshot>(cancellationToken)
            ?? new BasketSnapshot(Array.Empty<Nodes.BasketLine>(), 0m);
    }

    public async Task<BasketSnapshot> GetBasketAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{_baseUrl}/basket", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BasketSnapshot>(cancellationToken)
            ?? new BasketSnapshot(Array.Empty<Nodes.BasketLine>(), 0m);
    }

    public async Task<OrderHistoryResult> GetOrderHistoryAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync($"{_baseUrl}/order-history", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderHistoryResult>(cancellationToken)
            ?? new OrderHistoryResult(Array.Empty<OrderHistoryEntry>());
    }
}
