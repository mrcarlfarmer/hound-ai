namespace Hound.Core.Models;

/// <summary>
/// Full transcript of a single bull-vs-bear debate run inside <c>StrategyNode</c>,
/// persisted once per StrategyNode invocation. Backs the dashboard "Strategy
/// Debate" panel so it can load the conversation directly by run id
/// (<c>GET /api/debates/{runId}</c>) instead of reconstructing it from per-turn
/// <see cref="ActivityLog"/> rows via metadata filtering.
/// </summary>
/// <remarks>
/// <para>
/// Document id convention: <c>DebateRecords/{RunId}/{RefinementCount}</c>.
/// StrategyNode may run several times for one graph run during risk-refinement
/// loops; each invocation writes its own record so the complete debate history
/// is retained rather than overwritten. The API returns all records for a run
/// ordered by <see cref="RefinementCount"/>.
/// </para>
/// <para>
/// Retention: DebateRecords live in the <c>hound-trading-pack</c> database
/// alongside the <see cref="GraphRun"/> and activity documents they summarise,
/// and follow the same retention policy as <see cref="ActivityLog"/> (see the
/// "Data retention" section of the README).
/// </para>
/// </remarks>
public class DebateRecord
{
    /// <summary>RavenDB document id (<c>DebateRecords/{RunId}/{RefinementCount}</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Graph run this debate belongs to.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Ticker symbol the debate was held for.</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// Zero-based refinement iteration of the StrategyNode invocation that
    /// produced this debate. 0 is the first (pre-refinement) invocation.
    /// </summary>
    public int RefinementCount { get; set; }

    /// <summary>Configured number of turns each side spoke during the debate.</summary>
    public int TurnsPerSide { get; set; }

    /// <summary>When the debate transcript was captured.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The debate turns, captured in the order the debaters spoke.</summary>
    public List<DebateTurn> Turns { get; set; } = [];
}
