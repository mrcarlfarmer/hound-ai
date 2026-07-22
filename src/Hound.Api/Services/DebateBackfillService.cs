using System.Text.Json;
using Hound.Core.Logging;
using Hound.Core.Models;

namespace Hound.Api.Services;

/// <summary>
/// Reconstructs a <see cref="GraphRun.StrategyDebate"/> transcript by scanning
/// activity-log entries when the persisted snapshot is missing or empty.
/// </summary>
/// <remarks>
/// Two correlation strategies are tried in order:
/// <list type="number">
///   <item><description>Strict match on <c>metadata.runId</c> (present on debates
///   emitted after the runId-metadata change).</description></item>
///   <item><description>Fallback: <c>metadata.symbol</c> matches the run's symbol
///   AND the activity timestamp falls inside
///   <c>[run.StartedAt, run.CompletedAt ?? run.StartedAt + 1h]</c>. Catches
///   older debates emitted before runId was added to metadata.</description></item>
/// </list>
/// </remarks>
public class DebateBackfillService
{
    private const string PackId = "trading-pack";
    private static readonly TimeSpan FallbackWindow = TimeSpan.FromHours(1);

    private readonly IActivityLogger _activity;

    public DebateBackfillService(IActivityLogger activity)
    {
        _activity = activity;
    }

    /// <summary>
    /// Populates <see cref="GraphRun.StrategyDebate"/> from activity logs when
    /// the field is currently null or empty. Idempotent — runs that already
    /// have a persisted transcript are returned untouched.
    /// </summary>
    public async Task BackfillAsync(GraphRun run, CancellationToken cancellationToken)
    {
        if (run.StrategyDebate is { Count: > 0 }) return;
        var turns = await CollectTurnsAsync(run, cancellationToken);
        if (turns.Count > 0)
            run.StrategyDebate = turns;
    }

    /// <summary>Batch variant — runs <see cref="BackfillAsync"/> over each run.</summary>
    public async Task BackfillAsync(IEnumerable<GraphRun> runs, CancellationToken cancellationToken)
    {
        foreach (var run in runs)
            await BackfillAsync(run, cancellationToken);
    }

    private async Task<List<DebateTurn>> CollectTurnsAsync(GraphRun run, CancellationToken cancellationToken)
    {
        // Bound the activity query to the run's lifetime; +1h fallback gives
        // late-arriving entries room without dragging in unrelated runs on
        // the same symbol.
        var from = run.StartedAt.AddMinutes(-1);
        var to = (run.CompletedAt ?? run.StartedAt + FallbackWindow).AddMinutes(1);

        var activities = await _activity.GetActivitiesAsync(
            packId: PackId,
            houndId: "strategy-node",
            from: from,
            to: to,
            page: 1,
            pageSize: 200,
            cancellationToken: cancellationToken);

        var matched = new List<(DebateTurn Turn, DateTime At)>();
        foreach (var log in activities)
        {
            if (log.Metadata is null || log.Metadata.Count == 0) continue;
            if (GetString(log.Metadata, "type") != "debate-turn") continue;

            var runId = GetString(log.Metadata, "runId");
            var symbol = GetString(log.Metadata, "symbol");
            var isExactRun = runId == run.RunId;
            var isFallback = runId is null && symbol == run.Symbol;
            if (!isExactRun && !isFallback) continue;

            var role = GetString(log.Metadata, "role");
            var message = GetString(log.Metadata, "fullMessage");
            var turnIndex = GetInt(log.Metadata, "turnIndex");
            if (role is null || message is null || turnIndex is null) continue;

            matched.Add((new DebateTurn(role, turnIndex.Value, message, log.Timestamp), log.Timestamp));
        }

        // Activity rows arrived newest-first; debate transcript should be
        // chronological. Order by the captured turn index to be robust against
        // sub-second timestamp ties.
        return matched
            .OrderBy(m => m.Turn.Index)
            .ThenBy(m => m.At)
            .Select(m => m.Turn)
            .ToList();
    }

    private static string? GetString(IDictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var raw) || raw is null) return null;
        return raw switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => raw.ToString(),
        };
    }

    private static int? GetInt(IDictionary<string, object> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var raw) || raw is null) return null;
        return raw switch
        {
            int i => i,
            long l => (int)l,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var v) => v,
            string s when int.TryParse(s, out var v) => v,
            _ => null,
        };
    }
}
