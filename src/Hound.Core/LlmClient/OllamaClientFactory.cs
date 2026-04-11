namespace Hound.Core.LlmClient;

public class OllamaClientFactory : IOllamaClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public string BaseUrl { get; }

    public OllamaClientFactory(IHttpClientFactory httpClientFactory, string baseUrl = "http://ollama:11434/v1")
    {
        _httpClientFactory = httpClientFactory;
        BaseUrl = baseUrl;
    }

    public HttpClient CreateClient(string modelName)
    {
        // TODO: Implement in Wave 2 — configure client with model-specific headers/settings
        throw new NotImplementedException();
    }
}
