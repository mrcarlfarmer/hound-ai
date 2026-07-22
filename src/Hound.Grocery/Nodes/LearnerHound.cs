using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Graph;
using Hound.Grocery.Services;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Nodes;

/// <summary>
/// The "Tuner" analog and the <b>writer</b> side of the learning loop that
/// <see cref="PlannerHound"/>'s <see cref="PreferenceService"/> reads. Runs after
/// a shop to refine learned product mappings, typical quantities, favourite/staple
/// weighting and dislikes from <c>purchase-history.md</c>, then writes back to
/// <c>preferences.md</c> via an atomic read-merge-write so human-curated and
/// previously-learned entries are never clobbered (spec §8, §10).
/// <para>
/// All learning is deterministic (<see cref="PreferenceLearner"/>); both reader
/// and writer share the single <see cref="PreferencesSerializer"/> format.
/// </para>
/// </summary>
public class LearnerHound : INode
{
    public string NodeId => "learner-hound";
    public string PackId => GroceryPack.PackId;

    private readonly IActivityLogger _activityLogger;
    private readonly PreferenceService _preferences;
    private readonly PurchaseHistoryService _history;
    private readonly PreferenceLearner _learner;
    private readonly ILogger<LearnerHound>? _logger;

    public LearnerHound(
        IActivityLogger activityLogger,
        PreferenceService preferences,
        PurchaseHistoryService history,
        PreferenceLearner learner,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _preferences = preferences;
        _history = history;
        _learner = learner;
        _logger = loggerFactory?.CreateLogger<LearnerHound>();
    }

    public async Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken)
    {
        var observations = await _history.LoadAsync(cancellationToken);
        var result = await LearnAsync(observations, dislikes: [], cancellationToken);

        _logger?.LogInformation(
            "LearnerHound processed {Count} observation(s) → {Changes} change(s) for run {RunId}.",
            observations.Count, result.Changes.Count, state.RunId);

        return state with { CurrentNode = NodeId, Phase = GroceryPhase.Learning };
    }

    /// <summary>
    /// Merges the given observations and dislikes into <c>preferences.md</c> via an
    /// atomic read-merge-write, logs a <c>PreferencesUpdated</c> activity describing
    /// what changed, and returns the learning result. Exposed internally so tests can
    /// drive synthetic observations without touching the graph.
    /// </summary>
    internal async Task<LearnResult> LearnAsync(
        IReadOnlyList<PurchaseObservation> observations,
        IReadOnlyList<string> dislikes,
        CancellationToken cancellationToken)
    {
        LearnResult result = new(PreferencesDocument.Empty, []);

        await _preferences.UpdateAsync(current =>
        {
            result = _learner.Learn(current, observations, dislikes);
            return result.Document;
        }, cancellationToken);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(LearnerHound),
            Message = result.HasChanges
                ? $"Refined preferences: {string.Join("; ", result.Changes)}"
                : "Reviewed purchase history; no preference changes.",
            Severity = ActivitySeverity.Info,
            Metadata = new Dictionary<string, object>
            {
                ["event"] = "PreferencesUpdated",
                ["observations"] = observations.Count,
                ["changeCount"] = result.Changes.Count,
                ["changes"] = result.Changes.ToArray(),
            },
        }, cancellationToken);

        return result;
    }
}
