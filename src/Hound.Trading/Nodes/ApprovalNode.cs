using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.Graph;
using Microsoft.Extensions.Logging;

namespace Hound.Trading.Nodes;

/// <summary>
/// Human-in-the-loop approval gate. Sits between <c>RiskNode</c> and
/// <c>ExecutionNode</c>. On its first (and only) execution it flips
/// <see cref="TradingGraphState.ApprovalStatus"/> to <see cref="ApprovalStatus.Pending"/>,
/// after which the graph yields to the worker. The worker watches for a
/// <see cref="GraphApproval"/> document (written by the API when a user clicks
/// Approve/Reject) and either:
/// <list type="bullet">
/// <item>On <c>Approved</c>: updates the checkpoint and calls <c>ResumeAsync</c>.
/// The router then bypasses this node and routes straight to execution.</item>
/// <item>On <c>Rejected</c>: updates the checkpoint with a rejection
/// <see cref="ExecutionResult"/>, marks the run complete, and clears the checkpoint.</item>
/// </list>
/// </summary>
public class ApprovalNode : INode
{
    public string NodeId => "approval-node";
    public string PackId => "trading-pack";

    private readonly IActivityLogger _activityLogger;
    private readonly ILogger<ApprovalNode>? _logger;

    public ApprovalNode(
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _logger = loggerFactory?.CreateLogger<ApprovalNode>();
    }

    public async Task<TradingGraphState> ExecuteAsync(
        TradingGraphState state, CancellationToken cancellationToken)
    {
        // This node is only ever reached when ApprovalStatus is NotRequested.
        // (Approved/Rejected resumes are routed past this node by the graph.)
        var decision = state.RiskOutput?.Decision;

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "ApprovalNode",
            Message = decision is null
                ? $"Awaiting human approval for {state.Symbol}"
                : $"Awaiting human approval: {decision.Action} {decision.Quantity} {decision.Symbol}",
            Severity = ActivitySeverity.Info,
            Metadata = new Dictionary<string, object>
            {
                ["runId"] = state.RunId,
                ["symbol"] = state.Symbol,
            },
        }, cancellationToken);

        _logger?.LogInformation(
            "Run {RunId} paused at approval gate awaiting human decision", state.RunId);

        return state with
        {
            ApprovalStatus = ApprovalStatus.Pending,
            ApprovalRequestedAt = DateTime.UtcNow,
        };
    }
}
