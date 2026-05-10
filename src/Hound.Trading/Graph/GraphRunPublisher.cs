using System.Text.Json;
using System.Text.Json.Serialization;
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
        ["data-node", "strategy-node", "risk-node", "execution-node", "monitor-node"];

    private readonly IDocumentStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiBaseUrl;

    public GraphRunPublisher(IDocumentStore store, IHttpClientFactory httpClientFactory, string apiBaseUrl)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
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
        if (state.IsComplete)
            run.CompletedAt = DateTime.UtcNow;

        run.Nodes = BuildNodeSnapshots(state);

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

    private static List<NodeSnapshot> BuildNodeSnapshots(TradingGraphState state)
    {
        var nodes = new List<NodeSnapshot>();

        foreach (var nodeId in OrderedNodeIds)
        {
            var (status, outputJson) = nodeId switch
            {
                "data-node" => GetSlot(state.DataOutput, state.CurrentNode, nodeId),
                "strategy-node" => GetSlot(state.StrategyOutput, state.CurrentNode, nodeId),
                "risk-node" => GetSlot(state.RiskOutput, state.CurrentNode, nodeId),
                "execution-node" => GetSlot(state.ExecutionOutput, state.CurrentNode, nodeId),
                "monitor-node" => GetSlot(state.MonitorOutput, state.CurrentNode, nodeId),
                _ => (NodeStatus.Pending, null),
            };

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
}
