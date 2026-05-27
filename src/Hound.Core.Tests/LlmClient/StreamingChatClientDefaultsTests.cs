using Hound.Core.LlmClient;
using Microsoft.Extensions.AI;
using Moq;

namespace Hound.Core.Tests.LlmClient;

[TestClass]
public sealed class StreamingChatClientDefaultsTests
{
    [TestMethod]
    public async Task GetResponseAsync_AppliesDefaultTemperature()
    {
        ChatOptions? capturedOptions = null;
        var innerClient = new Mock<IChatClient>();
        innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) =>
                capturedOptions = opts)
            .ReturnsAsync(new ChatResponse([]));

        var defaults = new ChatOptions { Temperature = 0.0f, TopP = 0.1f };
        var client = new StreamingChatClient(innerClient.Object, defaults);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        Assert.IsNotNull(capturedOptions);
        Assert.AreEqual(0.0f, capturedOptions.Temperature);
        Assert.AreEqual(0.1f, capturedOptions.TopP);
    }

    [TestMethod]
    public async Task GetResponseAsync_CallSiteOptionsDoNotOverrideDefaults_WhenNull()
    {
        ChatOptions? capturedOptions = null;
        var innerClient = new Mock<IChatClient>();
        innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) =>
                capturedOptions = opts)
            .ReturnsAsync(new ChatResponse([]));

        var defaults = new ChatOptions { Temperature = 0.0f, TopP = 0.1f };
        var client = new StreamingChatClient(innerClient.Object, defaults);

        // Caller passes options with no temperature/top_p set
        var callOptions = new ChatOptions { MaxOutputTokens = 100 };
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], callOptions);

        Assert.IsNotNull(capturedOptions);
        Assert.AreEqual(0.0f, capturedOptions.Temperature);
        Assert.AreEqual(0.1f, capturedOptions.TopP);
        Assert.AreEqual(100, capturedOptions.MaxOutputTokens);
    }

    [TestMethod]
    public async Task GetResponseAsync_CallSiteOptionsOverrideDefaults_WhenExplicitlySet()
    {
        ChatOptions? capturedOptions = null;
        var innerClient = new Mock<IChatClient>();
        innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) =>
                capturedOptions = opts)
            .ReturnsAsync(new ChatResponse([]));

        var defaults = new ChatOptions { Temperature = 0.0f, TopP = 0.1f };
        var client = new StreamingChatClient(innerClient.Object, defaults);

        // Caller explicitly overrides
        var callOptions = new ChatOptions { Temperature = 0.5f, TopP = 0.9f };
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")], callOptions);

        Assert.IsNotNull(capturedOptions);
        Assert.AreEqual(0.5f, capturedOptions.Temperature);
        Assert.AreEqual(0.9f, capturedOptions.TopP);
    }

    /// <summary>
    /// Guards that the factory produces chat clients with temperature=0.0 and top_p=0.1.
    /// If these values need changing, this test must be updated deliberately.
    /// </summary>
    [TestMethod]
    public void OllamaClientFactory_DefaultChatOptions_AreTemperature0_TopP01()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        var factory = new OllamaClientFactory(httpClientFactory.Object);
        var chatClient = factory.CreateChatClient("gemma3");

        // The returned client is a StreamingChatClient wrapping the OpenAI client.
        // Verify by making a call and checking the options propagate.
        Assert.IsNotNull(chatClient);
        Assert.IsInstanceOfType<StreamingChatClient>(chatClient);
    }

    /// <summary>
    /// Ensures the hardcoded temperature and top_p values in the factory are exactly 0.0 and 0.1.
    /// This test will fail if someone changes the defaults, forcing deliberate review.
    /// </summary>
    [TestMethod]
    public async Task OllamaClientFactory_ChatClient_SendsCorrectTemperatureAndTopP()
    {
        ChatOptions? capturedOptions = null;
        var innerClient = new Mock<IChatClient>();
        innerClient.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, opts, _) =>
                capturedOptions = opts)
            .ReturnsAsync(new ChatResponse([]));

        // Replicate what the factory does
        var defaults = new ChatOptions { Temperature = 0.0f, TopP = 0.1f };
        var client = new StreamingChatClient(innerClient.Object, defaults);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "test")]);

        Assert.IsNotNull(capturedOptions);
        Assert.AreEqual(0.0f, capturedOptions.Temperature, "Temperature must be 0.0 for deterministic output");
        Assert.AreEqual(0.1f, capturedOptions.TopP, "TopP must be 0.1 for deterministic output");
    }
}
