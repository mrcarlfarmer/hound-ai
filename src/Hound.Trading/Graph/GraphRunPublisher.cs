using System.Text.Json;
using System.Text.Json.Serialization;
using Hound.Core.LlmClient;
using Hound.Core.Models;
using Hound.Trading.Nodes;
using Raven.Client.Documents;

namespace Hound.Trading.Graph;

/// <summary>
/// Writes <see cref="GraphRun"/> snapshots to RavenDB so the API can serve
/// run state to the dashboard. Also notifies the API via HTTP for real-time
/// SignalR broadcasts after each node transition.
/// </summary>
public class GraphRunPublisher
{
    private const string Database = "hound-trading-pack";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] OrderedNodeIds =
        ["analysts-team-node", "strategy-node", "risk-node", "approval-node", "execution-node", "monitor-node"];

    private readonly IDocumentStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiBaseUrl;
    private readonly INodeStreamPublisher? _reasoningSource;

    public GraphRunPublisher(
        IDocumentStore store,
        IHttpClientFactory httpClientFactory,
        string apiBaseUrl,
        INodeStreamPublisher? reasoningSource = null)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _reasoningSource = reasoningSource;
    }

    /// <summary>Publish the current graph state as a <see cref="GraphRun"/> snapshot.</summary>
    public async Task PublishAsync(TradingGraphState state, CancellationToken cancellationToken)
    {
        using var session = _store.OpenAsyncSession(Database);

        var docId = $"GraphRuns/{state.RunId}";
        var run = await session.LoadAsync<GraphRun>(docId, cancellationToken) ?? new GraphRun
        {
            Id = docId,
            RunId = state.RunId,
            Symbol = state.Symbol,
            StartedAt = state.StartedAt,
        };

        run.Phase = state.Phase;
        run.CurrentNode = state.CurrentNode;
        run.IsComplete = state.IsComplete;
        run.ErrorMessage = state.ErrorMessage;
        run.RefinementCount = state.RefinementCount;
        run.MonitorCycleCount = state.MonitorCycleCount;
        run.ApprovalStatus = state.ApprovalStatus;
        run.ApprovalDecidedBy = state.ApprovalDecidedBy;
        run.ApprovalDecidedAt = state.ApprovalDecidedAt;
        run.ApprovalNotes = state.ApprovalNotes;
        if (state.IsComplete)
            run.CompletedAt = DateTime.UtcNow;

        run.Refinements = state.RefinementHistory.Select(r => new RefinementSnapshot
        {
            Attempt = r.Attempt,
            Symbol = r.RejectedDecision.Symbol,
            Action = r.RejectedDecision.Action.ToString(),
            Quantity = r.RejectedDecision.Quantity,
            RiskReasoning = r.RiskReasoning,
            OccurredAt = r.OccurredAt,
        }).ToList();

        run.Nodes = BuildNodeSnapshots(state, run.Nodes);
        if (_reasoningSource is not null)
        {
            foreach (var snap in run.Nodes)
            {
                var reasoning = _reasoningSource.GetReasoning(state.RunId, snap.NodeId);
                if (!string.IsNullOrEmpty(reasoning))
                    snap.ReasoningText = reasoning;
            }
        }

        // Preserve the most recent StrategyNode debate transcript so the
        // dashboard can render it on the run-detail view. We copy only when
        // the current state has a fresh debate to avoid clobbering the
        // existing snapshot when downstream nodes (Risk, Execution) push
        // updates that don't include a debate of their own.
        if (state.StrategyDebate is { Count: > 0 } debate)
        {
            run.StrategyDebate = [.. debate];
        }

        await session.StoreAsync(run, docId, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);

        // Best-effort SignalR broadcast via the API
        try
        {
            var client = _httpClientFactory.CreateClient();
            var json = JsonSerializer.Serialize(run, JsonOptions);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await client.PostAsync($"{_apiBaseUrl}/api/runs/events/node-completed", content, cancellationToken);
        }
        catch
        {
            // Non-critical — dashboard will refresh via polling
        }
    }

    private static List<NodeSnapshot> BuildNodeSnapshots(TradingGraphState state, List<NodeSnapshot>? existing)
    {
        var existingLookup = existing?.ToDictionary(n => n.NodeId) ?? [];
        var nodes = new List<NodeSnapshot>();

        // When the graph has looped back into Strategy for a refinement attempt
        // (Risk rejected, StrategyOutput cleared, RefinementCount incremented),
        // the previous risk + strategy snapshots are no longer "the current
        // truth" — they belong to the prior attempt and live on in
        // RefinementHistory. Suppress their preserved Completed/Failed status
        // so the dashboard timeline accurately shows the in-flight attempt.
        var refinementInFlight = state.RefinementCount > 0
            && state.StrategyOutput is null
            && state.CurrentNode == "strategy-node";

        foreach (var nodeId in OrderedNodeIds)
        {
            var (status, outputJson) = nodeId switch
            {
                "analysts-team-node" => GetSlot(state.DataOutput, state.CurrentNode, nodeId),
                "strategy-node" => GetSlot(state.StrategyOutput, state.CurrentNode, nodeId),
                "risk-node" => GetSlot(state.RiskOutput, state.CurrentNode, nodeId),
                "approval-node" => GetApprovalSlot(state),
                "execution-node" => GetExecutionSlot(state),
                "monitor-node" => GetSlot(state.MonitorOutput, state.CurrentNode, nodeId),
                _ => (NodeStatus.Pending, null),
            };

            // While Strategy is being retried (refinement), the prior risk
            // assessment is still hanging off the state until the new attempt
            // overwrites it — show that slot as Pending so the dashboard
            // doesn't display a stale "Rejected" Risk card alongside the
            // in-flight Strategy.
            if (refinementInFlight && nodeId == "risk-node")
            {
                status = NodeStatus.Pending;
                outputJson = null;
            }

            // Preserve existing Completed/Failed status and output when the new
            // snapshot would regress to Pending (e.g. during monitor loop cycles
            // where prior node outputs aren't in the state object). Suppressed
            // during a refinement attempt so the prior risk-node snapshot
            // doesn't masquerade as the current truth.
            if (!refinementInFlight
                && status == NodeStatus.Pending
                && existingLookup.TryGetValue(nodeId, out var prev)
                && prev.Status is NodeStatus.Completed or NodeStatus.Failed)
            {
                status = prev.Status;
                outputJson = prev.OutputJson;
            }

            nodes.Add(new NodeSnapshot
            {
                NodeId = nodeId,
                Status = status,
                OutputJson = outputJson,
            });
        }

        return nodes;
    }

    private static (NodeStatus Status, string? OutputJson) GetSlot<T>(T? output, string? currentNode, string nodeId)
        where T : class
    {
        if (output is not null)
            return (NodeStatus.Completed, JsonSerializer.Serialize(output, JsonOptions));

        if (currentNode == nodeId)
            return (NodeStatus.Active, null);

        return (NodeStatus.Pending, null);
    }

    private static (NodeStatus Status, string? OutputJson) GetExecutionSlot(TradingGraphState state)
    {
        if (state.ExecutionOutput is not null)
        {
            var status = state.ExecutionOutput.Success ? NodeStatus.Completed : NodeStatus.Failed;
            return (status, JsonSerializer.Serialize(state.ExecutionOutput, JsonOptions));
        }

        if (state.CurrentNode == "execution-node")
            return (NodeStatus.Active, null);

        return (NodeStatus.Pending, null);
    }

    private static (NodeStatus Status, string? OutputJson) GetApprovalSlot(TradingGraphState state)
    {
        var payload = new
        {
            status = state.ApprovalStatus.ToString(),
            decidedBy = state.ApprovalDecidedBy,
            decidedAt = state.ApprovalDecidedAt,
            notes = state.ApprovalNotes,
            requestedAt = state.ApprovalRequestedAt,
        };

        return state.ApprovalStatus switch
        {
            ApprovalStatus.Approved => (NodeStatus.Completed, (string?)JsonSerializer.Serialize(payload, JsonOptions)),
            ApprovalStatus.Rejected => (NodeStatus.Failed, (string?)JsonSerializer.Serialize(payload, JsonOptions)),
            ApprovalStatus.Pending => (NodeStatus.Active, (string?)JsonSerializer.Serialize(payload, JsonOptions)),
            _ => state.CurrentNode == "approval-node"
                ? (NodeStatus.Active, (string?)null)
                : (NodeStatus.Pending, (string?)null),
        };
    }
}
