using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Graph;
using Hound.Grocery.Services;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Nodes;

/// <summary>
/// The "Tuner" analog. Runs on a timer / after each shop to refine learned
/// product mappings, typical quantities, favourite weighting and dislikes from
/// basket history, explicit user feedback and (optionally) the Sainsbury's
/// order-history page. Writes back to <c>preferences.md</c>.
/// <para>
/// <b>Phase 1 scaffold:</b> returns the state unchanged after logging. No
/// learning logic, embeddings or LLM calls are wired up yet (Phase 6).
/// </para>
/// </summary>
public class LearnerHound : INode
{
    public string NodeId => "learner-hound";
    public string PackId => GroceryPack.PackId;

    private readonly IActivityLogger _activityLogger;
    private readonly StateFileService _stateFiles;
    private readonly ILogger<LearnerHound>? _logger;

    public LearnerHound(
        IActivityLogger activityLogger,
        StateFileService stateFiles,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _stateFiles = stateFiles;
        _logger = loggerFactory?.CreateLogger<LearnerHound>();
    }

    public async Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("LearnerHound stub invoked for run {RunId}", state.RunId);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(LearnerHound),
            Message = "LearnerHound scaffold placeholder (Phase 1 — no behaviour yet)",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        return state with { CurrentNode = NodeId, Phase = GroceryPhase.Learning };
    }
}
