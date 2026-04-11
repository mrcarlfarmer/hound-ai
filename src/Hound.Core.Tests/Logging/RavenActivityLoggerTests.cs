using Hound.Core.Logging;
using Hound.Core.Models;
using Moq;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace Hound.Core.Tests.Logging;

[TestClass]
public sealed class RavenActivityLoggerTests
{
    private Mock<IDocumentStore> _storeMock = null!;
    private Mock<IAsyncDocumentSession> _sessionMock = null!;
    private RavenActivityLogger _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _storeMock = new Mock<IDocumentStore>();
        _sessionMock = new Mock<IAsyncDocumentSession>();

        _storeMock.Setup(s => s.OpenAsyncSession(It.IsAny<string>()))
            .Returns(_sessionMock.Object);

        _logger = new RavenActivityLogger(_storeMock.Object);
    }

    [TestMethod]
    public void Constructor_WithValidStore_DoesNotThrow()
    {
        var store = new Mock<IDocumentStore>();
        var logger = new RavenActivityLogger(store.Object);
        Assert.IsNotNull(logger);
    }

    [TestMethod]
    public async Task LogActivityAsync_StoresActivityInSession()
    {
        var activity = new ActivityLog
        {
            Id = "log-001",
            PackId = "trading-pack",
            HoundId = "analysis-hound",
            HoundName = "AnalysisHound",
            Message = "Test log entry",
            Severity = ActivitySeverity.Info
        };

        await _logger.LogActivityAsync(activity);

        _sessionMock.Verify(s => s.StoreAsync(activity, It.IsAny<CancellationToken>()), Times.Once);
        _sessionMock.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task LogActivityAsync_WithCancellationToken_PassesTokenToSession()
    {
        var activity = new ActivityLog { PackId = "trading-pack", HoundId = "risk-hound" };
        using var cts = new CancellationTokenSource();

        await _logger.LogActivityAsync(activity, cts.Token);

        _sessionMock.Verify(s => s.StoreAsync(activity, cts.Token), Times.Once);
        _sessionMock.Verify(s => s.SaveChangesAsync(cts.Token), Times.Once);
    }

    [TestMethod]
    public async Task LogActivityAsync_OpensSessionWithPackDatabase()
    {
        var activity = new ActivityLog { PackId = "trading-pack" };

        await _logger.LogActivityAsync(activity);

        _storeMock.Verify(s => s.OpenAsyncSession("hound-trading-pack"), Times.Once);
    }

    [TestMethod]
    public async Task GetActivitiesAsync_OpensSessionForPack()
    {
        _sessionMock.Setup(s => s.Query<ActivityLog>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Throws<InvalidOperationException>();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _logger.GetActivitiesAsync(packId: "trading-pack"));

        _storeMock.Verify(s => s.OpenAsyncSession("hound-trading-pack"), Times.Once);
    }

    [TestMethod]
    public async Task GetActivitiesAsync_NoPack_UsesDefaultDatabase()
    {
        _sessionMock.Setup(s => s.Query<ActivityLog>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Throws<InvalidOperationException>();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => _logger.GetActivitiesAsync());

        _storeMock.Verify(s => s.OpenAsyncSession("hound-activity"), Times.Once);
    }
}
