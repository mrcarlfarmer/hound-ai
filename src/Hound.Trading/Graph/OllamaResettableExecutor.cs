using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Hound.Trading.Graph;

/// <summary>
/// Unloads Ollama models by posting <c>keep_alive: "0"</c> to the generate endpoint,
/// freeing VRAM between monitor loop iterations.
/// </summary>
public class OllamaResettableExecutor : IResettableExecutor
{
    private static readonly string[] Models = ["qwen3:14b", "qwen3.5:9b"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _ollamaBaseUrl;
    private readonly ILogger<OllamaResettableExecutor>? _logger;

    public OllamaResettableExecutor(
        IHttpClientFactory httpClientFactory,
        string ollamaBaseUrl = "http://ollama:11434",
        ILogger<OllamaResettableExecutor>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _ollamaBaseUrl = ollamaBaseUrl.TrimEnd('/');
        _logger = logger;
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("ollama-reset");

        foreach (var model in Models)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    model,
                    keep_alive = "0",
                });

                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(
                    $"{_ollamaBaseUrl}/api/generate", content, cancellationToken);

                _logger?.LogDebug("Unloaded model {Model}: {StatusCode}", model, response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to unload model {Model}", model);
            }
        }
    }
}
