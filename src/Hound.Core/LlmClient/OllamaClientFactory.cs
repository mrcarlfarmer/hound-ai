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
    /// for the specified model. The returned client is wrapped with <see cref="StreamingChatClient"/>
    /// middleware so that node executions can stream reasoning to the dashboard.
    /// </summary>
    public IChatClient CreateChatClient(string modelName)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(BaseUrl),
        };
        var credential = new ApiKeyCredential("ollama");
        var chatClient = new OpenAI.Chat.ChatClient(modelName, credential, options);

        var defaultChatOptions = new ChatOptions
        {
            Temperature = 0.0f,
            TopP = 0.1f,
            // Greedy sampling (temp=0 + low top_p) is prone to repetition loops
            // where the model finds a plausible closing sentence and emits it
            // ad infinitum. A modest frequency penalty breaks the loop without
            // meaningfully impacting structured-JSON outputs (which never repeat
            // long phrases by design).
            FrequencyPenalty = 0.4f,
            // Hard ceiling so a runaway analyst can't blow past the context
            // window. ~2048 tokens ≈ 8-10 KB of text — generous for an analyst
            // report, but caps the worst-case at ≈3x normal length instead of
            // the 48 KB tails we've observed during repetition loops.
            MaxOutputTokens = 2048,
        };

        return new StreamingChatClient(chatClient.AsIChatClient(), defaultChatOptions);
    }
}
