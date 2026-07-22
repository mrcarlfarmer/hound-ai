using Hound.Core.Logging;
using Hound.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Hound.Trading.Nodes.Analysts;

/// <summary>
/// Abstract base for the specialist analysts owned by
/// <see cref="AnalystsTeamNode"/>. Centralises the activity-log + timeout +
/// repetition-loop-defensive-truncation plumbing so each derived analyst
/// only has to declare its prompt and its tool surface.
/// </summary>
public abstract class AnalystBase
{
    // Activity-log identity is shared across the whole analyst team so the
    // dashboard groups every analyst under the same hound entry. The per-
    // analyst distinction lives in the log message.
    private const string PackId = "trading-pack";
    private const string NodeId = "analysts-team-node";
    private const string HoundName = "AnalystsTeam";

    /// <summary>
    /// Soft upper bound on the per-analyst report size sent to the synthesiser
    /// (and persisted on the run snapshot). Sized to comfortably hold the
    /// largest healthy report we observe (~5 KB) with headroom (~3x), while
    /// still capping the worst-case repetition-loop tail.
    /// </summary>
    internal const int MaxAnalystReportChars = 16_000;

    // Agent is assigned by derived constructors via Configure(...) after they
    // have wired their instance-method tool delegates. We can't pass it in
    // the base ctor because the tools need to bind to `this` (which doesn't
    // exist until base ctor returns).
    private ChatClientAgent _agent = null!;
    protected ChatClientAgent Agent => _agent
        ?? throw new InvalidOperationException($"{Name} did not call Configure(agent) from its constructor.");

    protected IActivityLogger ActivityLogger { get; }

    /// <summary>
    /// Display name surfaced in activity logs (e.g. "MarketAnalyst").
    /// </summary>
    public abstract string Name { get; }

    protected AnalystBase(IActivityLogger activityLogger)
    {
        ActivityLogger = activityLogger;
    }

    /// <summary>
    /// Called by derived constructors once they have built the underlying
    /// <see cref="ChatClientAgent"/> (after wiring any instance-method tools).
    /// </summary>
    protected void Configure(ChatClientAgent agent) => _agent = agent;

    /// <summary>
    /// Runs the analyst against a single prompt with a wall-clock budget.
    /// Always returns a non-null report — on timeout or empty model output a
    /// stub string is returned so the synthesiser can treat the analyst as
    /// abstaining instead of blocking the graph.
    /// </summary>
    public async Task<string> AnalyseAsync(
        string symbol, string prompt, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await ActivityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = HoundName,
            Message = $"{Name} analysing {symbol}...",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var session = await Agent.CreateSessionAsync(cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        string report;
        try
        {
            var response = await Agent.RunAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                session,
                cancellationToken: cts.Token);
            report = response.Text ?? $"No report from {Name}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await ActivityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = NodeId,
                HoundName = HoundName,
                Message = $"{Name} exceeded {timeout.TotalSeconds:F0}s budget for "
                    + $"{symbol}; aborting analyst and continuing with a stub report.",
                Severity = ActivitySeverity.Warning,
            }, cancellationToken);
            return $"[{Name} aborted by orchestrator after {timeout.TotalSeconds:F0}s timeout — "
                + "likely a model repetition loop. Treat this analyst as abstaining.]";
        }

        return TruncateReport(report, symbol, cancellationToken);
    }

    /// <summary>
    /// Trims an analyst report to <see cref="MaxAnalystReportChars"/> and
    /// appends a clear truncation marker so the synthesiser doesn't treat the
    /// abrupt cut-off as part of the analyst's signal. Healthy reports
    /// (~3-5 KB) pass through untouched.
    /// </summary>
    private string TruncateReport(string report, string symbol, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(report) || report.Length <= MaxAnalystReportChars)
            return report;

        var originalLength = report.Length;
        var truncated = report.Substring(0, MaxAnalystReportChars)
            + $"\n\n[…report truncated by orchestrator: original was {originalLength:N0} chars, "
            + $"capped at {MaxAnalystReportChars:N0}. Likely a model repetition loop — "
            + "treat any trailing content as low signal.]";

        // Fire-and-forget log; truncation is a real signal worth surfacing on
        // the activity feed but we don't want to block synthesis on it.
        _ = ActivityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = HoundName,
            Message = $"{Name} report for {symbol} truncated from {originalLength:N0} to "
                + $"{MaxAnalystReportChars:N0} chars (likely repetition loop).",
            Severity = ActivitySeverity.Warning,
        }, cancellationToken);

        return truncated;
    }

    /// <summary>
    /// Small helper for truncating long strings (article summaries, social
    /// messages) shown inside an analyst's tool output.
    /// </summary>
    protected static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
        return value[..max].TrimEnd() + "…";
    }
}
