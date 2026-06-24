using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Graph;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Nodes;

/// <summary>
/// Telegram front door + persona. Parses natural-language messages
/// (add/remove/query/commands), maintains <c>shopping-list.md</c>, relays
/// notifications and runs the approval/trim conversation.
/// <para>
/// <b>Phase 1 scaffold:</b> this node is a no-op placeholder — it logs that it
/// ran and returns the state unchanged. No Telegram polling and no LLM calls are
/// wired up yet (Phase 2).
/// </para>
/// </summary>
public class ConciergeHound : INode
{
    public string NodeId => "concierge-hound";
    public string PackId => GroceryPack.PackId;

    private readonly IActivityLogger _activityLogger;
    private readonly ILogger<ConciergeHound>? _logger;

    public ConciergeHound(IActivityLogger activityLogger, ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _logger = loggerFactory?.CreateLogger<ConciergeHound>();
    }

    public async Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("ConciergeHound stub invoked for run {RunId}", state.RunId);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(ConciergeHound),
            Message = "ConciergeHound scaffold placeholder (Phase 1 — no behaviour yet)",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        return state with { CurrentNode = NodeId, Phase = GroceryPhase.Reporting };
    }
}
