using Hound.Api.Repositories;
using Hound.Api.Services;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Hound.Api.Controllers;

[ApiController]
[Route("api/tuner")]
public class TunerController : ControllerBase
{
    private readonly ITunerExperimentRepository _repository;
    private readonly TunerStateService _tunerState;
    private readonly string _configDirectory;

    public TunerController(
        ITunerExperimentRepository repository,
        TunerStateService tunerState,
        IConfiguration configuration)
    {
        _repository = repository;
        _tunerState = tunerState;
        _configDirectory = configuration["Tuner:ConfigDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "Config");
    }

    /// <summary>
    /// GET /api/tuner/experiments — Paginated list of tuner experiments.
    /// </summary>
    [HttpGet("experiments")]
    public async Task<ActionResult<IEnumerable<TunerExperiment>>> GetExperiments(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var results = await _repository.GetExperimentsAsync(page, pageSize, cancellationToken);
        return Ok(results);
    }

    /// <summary>
    /// GET /api/tuner/experiments/{id} — Single experiment detail.
    /// </summary>
    [HttpGet("experiments/{id}")]
    public async Task<ActionResult<TunerExperiment>> GetExperiment(
        string id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _repository.GetExperimentAsync(id, cancellationToken);
        if (experiment is null)
            return NotFound();
        return Ok(experiment);
    }

    /// <summary>
    /// POST /api/tuner/experiments/{id}/apply — Applies the candidate config to the server-side config
    /// directory and marks the experiment as applied.
    /// </summary>
    [HttpPost("experiments/{id}/apply")]
    public async Task<ActionResult> ApplyExperiment(
        string id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _repository.GetExperimentAsync(id, cancellationToken);
        if (experiment is null)
            return NotFound();

        if (experiment.Status == TunerExperimentStatus.Applied)
            return Conflict(new { message = "Experiment has already been applied." });

        if (string.IsNullOrWhiteSpace(experiment.ConfigAfter))
            return UnprocessableEntity(new { message = "Experiment has no candidate config to apply." });

        // Validate HoundName to prevent path traversal — only allow known safe file names
        if (!IsValidHoundName(experiment.HoundName))
            return UnprocessableEntity(new { message = $"Invalid hound name: '{experiment.HoundName}'." });

        var configPath = Path.Combine(_configDirectory, $"{experiment.HoundName}.json");

        try
        {
            Directory.CreateDirectory(_configDirectory);
            await System.IO.File.WriteAllTextAsync(configPath, experiment.ConfigAfter, cancellationToken);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Failed to write config file: {ex.Message}" });
        }

        await _repository.UpdateStatusAsync(id, TunerExperimentStatus.Applied, cancellationToken);
        return Ok(new { message = $"Config for {experiment.HoundName} updated and experiment marked as applied." });
    }

    /// <summary>
    /// POST /api/tuner/experiments/{id}/reject — Marks the experiment as rejected.
    /// </summary>
    [HttpPost("experiments/{id}/reject")]
    public async Task<ActionResult> RejectExperiment(
        string id,
        CancellationToken cancellationToken = default)
    {
        var experiment = await _repository.GetExperimentAsync(id, cancellationToken);
        if (experiment is null)
            return NotFound();

        await _repository.UpdateStatusAsync(id, TunerExperimentStatus.Rejected, cancellationToken);
        return Ok(new { message = $"Experiment {id} marked as rejected." });
    }

    /// <summary>
    /// POST /api/tuner/pause — Records pause intent for the tuner. The trading-pack's
    /// TunerHostedService reads this state on a shared signal (e.g., via RavenDB or flag).
    /// </summary>
    [HttpPost("pause")]
    public ActionResult Pause()
    {
        _tunerState.Pause();
        return Ok(new { message = "Tuner pause requested.", isPaused = true });
    }

    /// <summary>
    /// POST /api/tuner/resume — Clears pause intent for the tuner. The trading-pack's
    /// TunerHostedService reads this state on a shared signal (e.g., via RavenDB or flag).
    /// </summary>
    [HttpPost("resume")]
    public ActionResult Resume()
    {
        _tunerState.Resume();
        return Ok(new { message = "Tuner resume requested.", isPaused = false });
    }

    private static bool IsValidHoundName(string houndName) =>
        houndName is "StrategyHound" or "RiskHound" or "AnalysisHound" or "ExecutionHound";
}
