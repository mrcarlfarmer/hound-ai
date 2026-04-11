namespace Hound.Core.LlmClient;

public interface IOllamaClientFactory
{
    /// <summary>
    /// Creates an HTTP client configured for the Ollama OpenAI-compatible endpoint.
    /// </summary>
    /// <param name="modelName">The model to use (e.g., "gemma3", "qwen2.5", "phi3").</param>
    HttpClient CreateClient(string modelName);

    /// <summary>
    /// Gets the base URL for the Ollama API.
    /// </summary>
    string BaseUrl { get; }
}
