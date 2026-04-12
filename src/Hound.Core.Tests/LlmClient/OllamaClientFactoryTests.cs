using Hound.Core.LlmClient;
using Moq;

namespace Hound.Core.Tests.LlmClient;

[TestClass]
public sealed class OllamaClientFactoryTests
{
    [TestMethod]
    public void Constructor_DefaultBaseUrl_IsOllamaEndpoint()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var factory = new OllamaClientFactory(httpClientFactory.Object);

        Assert.AreEqual("http://ollama:11434/v1", factory.BaseUrl);
    }

    [TestMethod]
    public void Constructor_CustomBaseUrl_IsSet()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        const string customUrl = "http://localhost:11434/v1";

        var factory = new OllamaClientFactory(httpClientFactory.Object, customUrl);

        Assert.AreEqual(customUrl, factory.BaseUrl);
    }

    [TestMethod]
    public void Constructor_WithHttpClientFactory_DoesNotThrow()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var factory = new OllamaClientFactory(httpClientFactory.Object);
        Assert.IsNotNull(factory);
    }

    [TestMethod]
    public void CreateClient_ReturnsHttpClientWithBaseAddress()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        var factory = new OllamaClientFactory(httpClientFactory.Object);

        using var client = factory.CreateClient("gemma3");
        Assert.IsNotNull(client);
        Assert.AreEqual(new Uri(factory.BaseUrl), client.BaseAddress);
    }

    [TestMethod]
    public void CreateClient_UsesModelNameInClientName()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient("ollama-qwen2.5"))
            .Returns(new HttpClient());

        var factory = new OllamaClientFactory(httpClientFactory.Object);

        using var client = factory.CreateClient("qwen2.5");
        Assert.IsNotNull(client);
        httpClientFactory.Verify(f => f.CreateClient("ollama-qwen2.5"), Times.Once);
    }

    [TestMethod]
    public void BaseUrl_ImplementsInterface()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        IOllamaClientFactory factory = new OllamaClientFactory(httpClientFactory.Object);

        Assert.IsNotNull(factory.BaseUrl);
        Assert.IsTrue(factory.BaseUrl.StartsWith("http"));
    }
}
