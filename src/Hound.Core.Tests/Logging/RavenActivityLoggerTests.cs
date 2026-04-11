using Hound.Core.Logging;
using Hound.Core.Models;
using Moq;
using Raven.Client.Documents;

namespace Hound.Core.Tests.Logging;

[TestClass]
public sealed class RavenActivityLoggerTests
{
    private Mock<IDocumentStore> _storeMock = null!;
    private RavenActivityLogger _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _storeMock = new Mock<IDocumentStore>();
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
    public async Task LogActivityAsync_NotYetImplemented_ThrowsNotImplementedException()
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

        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _logger.LogActivityAsync(activity));
    }

    [TestMethod]
    public async Task LogActivityAsync_WithCancellationToken_ThrowsNotImplementedException()
    {
        var activity = new ActivityLog { PackId = "trading-pack", HoundId = "risk-hound" };
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _logger.LogActivityAsync(activity, cts.Token));
    }

    [TestMethod]
    public async Task GetActivitiesAsync_NotYetImplemented_ThrowsNotImplementedException()
    {
        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _logger.GetActivitiesAsync());
    }

    [TestMethod]
    public async Task GetActivitiesAsync_WithFilters_ThrowsNotImplementedException()
    {
        await Assert.ThrowsExceptionAsync<NotImplementedException>(
            () => _logger.GetActivitiesAsync(
                packId: "trading-pack",
                houndId: "execution-hound",
                from: DateTime.UtcNow.AddDays(-1),
                to: DateTime.UtcNow,
                page: 1,
                pageSize: 25));
    }
}
