using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

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
        var httpClient = _httpClientFactory.CreateClient($"ollama-{modelName}");
        httpClient.BaseAddress = new Uri(BaseUrl);
        return httpClient;
    }

    /// <summary>
    /// Creates an <see cref="IChatClient"/> configured for the Ollama OpenAI-compatible endpoint
    /// for the specified model.
    /// </summary>
    public IChatClient CreateChatClient(string modelName)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(BaseUrl),
        };
        var credential = new ApiKeyCredential("ollama");
        var chatClient = new OpenAI.Chat.ChatClient(modelName, credential, options);
        return chatClient.AsIChatClient();
    }
}
