using Hound.Api.Controllers;
using Hound.Api.Services;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Hound.Api.Tests.Controllers;

[TestClass]
public class HealthControllerTests
{
    private Mock<IHealthCheckService> _mockHealthService = null!;
    private HealthController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockHealthService = new Mock<IHealthCheckService>();
        _controller = new HealthController(_mockHealthService.Object);
    }

    [TestMethod]
    public async Task GetHealth_ReturnsOkWithReport()
    {
        var report = new HealthReport
        {
            Status = HealthStatus.Healthy,
            Timestamp = DateTime.UtcNow,
            Services =
            [
                new ServiceHealth { Name = "hound-api", Status = HealthStatus.Healthy },
                new ServiceHealth { Name = "ravendb", Status = HealthStatus.Healthy },
                new ServiceHealth { Name = "ollama", Status = HealthStatus.Healthy, Detail = "2 models loaded" },
                new ServiceHealth { Name = "trading-pack", Status = HealthStatus.Healthy, Detail = "Last activity 1m ago" }
            ]
        };
        _mockHealthService.Setup(s => s.CheckAllAsync(default)).ReturnsAsync(report);

        var result = await _controller.GetHealth(default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
        var value = ok.Value as HealthReport;
        Assert.IsNotNull(value);
        Assert.AreEqual(HealthStatus.Healthy, value.Status);
        Assert.AreEqual(4, value.Services.Count);
    }

    [TestMethod]
    public async Task GetHealth_WhenDegraded_ReturnsReportWithDegradedStatus()
    {
        var report = new HealthReport
        {
            Status = HealthStatus.Degraded,
            Timestamp = DateTime.UtcNow,
            Services =
            [
                new ServiceHealth { Name = "hound-api", Status = HealthStatus.Healthy },
                new ServiceHealth { Name = "ollama", Status = HealthStatus.Degraded, Detail = "No models loaded" }
            ]
        };
        _mockHealthService.Setup(s => s.CheckAllAsync(default)).ReturnsAsync(report);

        var result = await _controller.GetHealth(default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var value = ok.Value as HealthReport;
        Assert.IsNotNull(value);
        Assert.AreEqual(HealthStatus.Degraded, value.Status);
    }

    [TestMethod]
    public async Task GetHealth_ReturnsAllServiceNames()
    {
        var report = new HealthReport
        {
            Status = HealthStatus.Healthy,
            Timestamp = DateTime.UtcNow,
            Services =
            [
                new ServiceHealth { Name = "hound-api", Status = HealthStatus.Healthy },
                new ServiceHealth { Name = "ravendb", Status = HealthStatus.Healthy },
                new ServiceHealth { Name = "ollama", Status = HealthStatus.Healthy, Detail = "1 model loaded" },
                new ServiceHealth { Name = "trading-pack", Status = HealthStatus.Unknown, Detail = "No activity recorded" }
            ]
        };
        _mockHealthService.Setup(s => s.CheckAllAsync(default)).ReturnsAsync(report);

        var result = await _controller.GetHealth(default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var value = ok.Value as HealthReport;
        Assert.IsNotNull(value);
        var names = value.Services.Select(s => s.Name).ToList();
        CollectionAssert.Contains(names, "hound-api");
        CollectionAssert.Contains(names, "ravendb");
        CollectionAssert.Contains(names, "ollama");
        CollectionAssert.Contains(names, "trading-pack");
    }
}
