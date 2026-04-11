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
    public void CreateClient_NotYetImplemented_ThrowsNotImplementedException()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var factory = new OllamaClientFactory(httpClientFactory.Object);

        Assert.ThrowsException<NotImplementedException>(
            () => factory.CreateClient("gemma3"));
    }

    [TestMethod]
    public void CreateClient_AnyModelName_ThrowsNotImplementedException()
    {
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var factory = new OllamaClientFactory(httpClientFactory.Object);

        Assert.ThrowsException<NotImplementedException>(
            () => factory.CreateClient("qwen2.5"));

        Assert.ThrowsException<NotImplementedException>(
            () => factory.CreateClient("phi3"));
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
