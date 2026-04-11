using Alpaca.Markets;
using Hound.Trading.AlpacaClient;
using Microsoft.Extensions.Options;
using Moq;

namespace Hound.Trading.Tests.AlpacaClient;

[TestClass]
public sealed class AlpacaServiceTests
{
    private static IOptions<AlpacaSettings> CreateTestOptions() =>
        Options.Create(new AlpacaSettings
        {
            ApiKeyId = "test-key-id",
            SecretKey = "test-secret-key",
            Environment = "Paper"
        });

    private AlpacaService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _service = new AlpacaService(CreateTestOptions());
    }

    [TestCleanup]
    public void Cleanup()
    {
        _service.Dispose();
    }

    [TestMethod]
    public void AlpacaService_CanBeConstructed()
    {
        using var service = new AlpacaService(CreateTestOptions());
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void AlpacaService_ImplementsInterface()
    {
        Assert.IsInstanceOfType<IAlpacaService>(_service);
    }

    [TestMethod]
    public void AlpacaService_ImplementsDisposable()
    {
        Assert.IsInstanceOfType<IDisposable>(_service);
    }

    [TestMethod]
    public void AlpacaService_DefaultEnvironment_IsPaper()
    {
        var settings = new AlpacaSettings();
        Assert.AreEqual("Paper", settings.Environment);
    }

    [TestMethod]
    public void AlpacaSettings_SectionName_IsAlpaca()
    {
        Assert.AreEqual("Alpaca", AlpacaSettings.SectionName);
    }
}
