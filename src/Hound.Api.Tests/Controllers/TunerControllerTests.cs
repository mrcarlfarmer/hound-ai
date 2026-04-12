using Hound.Api.Controllers;
using Hound.Api.Repositories;
using Hound.Api.Services;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Hound.Api.Tests.Controllers;

[TestClass]
public class TunerControllerTests
{
    private Mock<ITunerExperimentRepository> _mockRepo = null!;
    private TunerStateService _tunerState = null!;
    private TunerController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<ITunerExperimentRepository>();
        _tunerState = new TunerStateService();
        var configuration = new ConfigurationBuilder().Build();
        _controller = new TunerController(_mockRepo.Object, _tunerState, configuration);
    }

    // ── GetExperiments ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetExperiments_ReturnsOkWithResults()
    {
        var experiments = new List<TunerExperiment>
        {
            new() { Id = "TunerExperiments/1", HoundName = "StrategyHound", Status = TunerExperimentStatus.PendingReview }
        };
        _mockRepo
            .Setup(r => r.GetExperimentsAsync(1, 20, default))
            .ReturnsAsync(experiments);

        var result = await _controller.GetExperiments(cancellationToken: default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
    }

    [TestMethod]
    public async Task GetExperiments_ReturnsEmptyList_WhenNoResults()
    {
        _mockRepo
            .Setup(r => r.GetExperimentsAsync(1, 20, default))
            .ReturnsAsync(new List<TunerExperiment>());

        var result = await _controller.GetExperiments(cancellationToken: default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var value = ok!.Value as IReadOnlyList<TunerExperiment>;
        Assert.IsNotNull(value);
        Assert.AreEqual(0, value.Count);
    }

    // ── GetExperiment ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetExperiment_ReturnsOk_WhenFound()
    {
        var experiment = new TunerExperiment
        {
            Id = "TunerExperiments/1",
            HoundName = "RiskHound",
            Status = TunerExperimentStatus.Improved
        };
        _mockRepo
            .Setup(r => r.GetExperimentAsync("TunerExperiments/1", default))
            .ReturnsAsync(experiment);

        var result = await _controller.GetExperiment("TunerExperiments/1", default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
        var value = ok.Value as TunerExperiment;
        Assert.IsNotNull(value);
        Assert.AreEqual("RiskHound", value.HoundName);
    }

    [TestMethod]
    public async Task GetExperiment_ReturnsNotFound_WhenMissing()
    {
        _mockRepo
            .Setup(r => r.GetExperimentAsync("missing-id", default))
            .ReturnsAsync((TunerExperiment?)null);

        var result = await _controller.GetExperiment("missing-id", default);

        Assert.IsInstanceOfType<NotFoundResult>(result.Result);
    }

    // ── ApplyExperiment ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task ApplyExperiment_ReturnsNotFound_WhenMissing()
    {
        _mockRepo
            .Setup(r => r.GetExperimentAsync("missing-id", default))
            .ReturnsAsync((TunerExperiment?)null);

        var result = await _controller.ApplyExperiment("missing-id", cancellationToken: default);

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task ApplyExperiment_ReturnsConflict_WhenAlreadyApplied()
    {
        var experiment = new TunerExperiment
        {
            Id = "TunerExperiments/1",
            HoundName = "StrategyHound",
            Status = TunerExperimentStatus.Applied,
            ConfigAfter = "{}"
        };
        _mockRepo
            .Setup(r => r.GetExperimentAsync("TunerExperiments/1", default))
            .ReturnsAsync(experiment);

        var result = await _controller.ApplyExperiment("TunerExperiments/1", cancellationToken: default);

        Assert.IsInstanceOfType<ConflictObjectResult>(result);
    }

    [TestMethod]
    public async Task ApplyExperiment_ReturnsUnprocessable_WhenNoConfigAfter()
    {
        var experiment = new TunerExperiment
        {
            Id = "TunerExperiments/1",
            HoundName = "StrategyHound",
            Status = TunerExperimentStatus.PendingReview,
            ConfigAfter = string.Empty
        };
        _mockRepo
            .Setup(r => r.GetExperimentAsync("TunerExperiments/1", default))
            .ReturnsAsync(experiment);

        var result = await _controller.ApplyExperiment("TunerExperiments/1", cancellationToken: default);

        Assert.IsInstanceOfType<UnprocessableEntityObjectResult>(result);
    }

    [TestMethod]
    public async Task ApplyExperiment_ReturnsUnprocessable_WhenHoundNameInvalid()
    {
        var experiment = new TunerExperiment
        {
            Id = "TunerExperiments/3",
            HoundName = "../etc/passwd",
            Status = TunerExperimentStatus.PendingReview,
            ConfigAfter = "{}"
        };
        _mockRepo
            .Setup(r => r.GetExperimentAsync("TunerExperiments/3", default))
            .ReturnsAsync(experiment);

        var result = await _controller.ApplyExperiment("TunerExperiments/3", cancellationToken: default);

        Assert.IsInstanceOfType<UnprocessableEntityObjectResult>(result);
    }

    [TestMethod]
    public async Task ApplyExperiment_WritesFileAndReturnsOk()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tuner:ConfigDirectory"] = tempDir
                })
                .Build();

            var controller = new TunerController(_mockRepo.Object, _tunerState, configuration);

            var experiment = new TunerExperiment
            {
                Id = "TunerExperiments/1",
                HoundName = "StrategyHound",
                Status = TunerExperimentStatus.PendingReview,
                ConfigAfter = "{\"Model\":\"gemma3\",\"Temperature\":0.05}"
            };
            _mockRepo
                .Setup(r => r.GetExperimentAsync("TunerExperiments/1", default))
                .ReturnsAsync(experiment);
            _mockRepo
                .Setup(r => r.UpdateStatusAsync("TunerExperiments/1", TunerExperimentStatus.Applied, default))
                .Returns(Task.CompletedTask);

            var result = await controller.ApplyExperiment("TunerExperiments/1", cancellationToken: default);

            Assert.IsInstanceOfType<OkObjectResult>(result);
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, "StrategyHound.json")));
            var written = await File.ReadAllTextAsync(Path.Combine(tempDir, "StrategyHound.json"));
            Assert.AreEqual(experiment.ConfigAfter, written);
            _mockRepo.Verify(
                r => r.UpdateStatusAsync("TunerExperiments/1", TunerExperimentStatus.Applied, default),
                Times.Once);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── RejectExperiment ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task RejectExperiment_ReturnsNotFound_WhenMissing()
    {
        _mockRepo
            .Setup(r => r.GetExperimentAsync("missing-id", default))
            .ReturnsAsync((TunerExperiment?)null);

        var result = await _controller.RejectExperiment("missing-id", default);

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task RejectExperiment_SetsStatusAndReturnsOk()
    {
        var experiment = new TunerExperiment
        {
            Id = "TunerExperiments/2",
            HoundName = "RiskHound",
            Status = TunerExperimentStatus.PendingReview
        };
        _mockRepo
            .Setup(r => r.GetExperimentAsync("TunerExperiments/2", default))
            .ReturnsAsync(experiment);
        _mockRepo
            .Setup(r => r.UpdateStatusAsync("TunerExperiments/2", TunerExperimentStatus.Rejected, default))
            .Returns(Task.CompletedTask);

        var result = await _controller.RejectExperiment("TunerExperiments/2", default);

        Assert.IsInstanceOfType<OkObjectResult>(result);
        _mockRepo.Verify(
            r => r.UpdateStatusAsync("TunerExperiments/2", TunerExperimentStatus.Rejected, default),
            Times.Once);
    }

    // ── Pause / Resume ────────────────────────────────────────────────────────

    [TestMethod]
    public void Pause_ReturnOkAndSetsPausedState()
    {
        var result = _controller.Pause();

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.IsTrue(_tunerState.IsPaused);
    }

    [TestMethod]
    public void Resume_ReturnsOkAndClearsPausedState()
    {
        _tunerState.Pause();

        var result = _controller.Resume();

        Assert.IsInstanceOfType<OkObjectResult>(result);
        Assert.IsFalse(_tunerState.IsPaused);
    }
}
