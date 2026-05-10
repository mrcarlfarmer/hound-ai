using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Trading.Graph;

public class TradingGraphSettings
{
    public const string SectionName = "TradingGraph";

    /// <summary>Symbols to analyse each run.</summary>
    public List<string> Symbols { get; set; } = [];

    /// <summary>Maximum times RiskNode can reject before hard-reject.</summary>
    public int MaxRefinements { get; set; } = 2;

    /// <summary>Seconds to wait between monitor loop iterations.</summary>
    public int MonitorDelaySeconds { get; set; } = 60;

    /// <summary>Maximum concurrent inference calls. Must be 1 for sequential inference on single GPU.</summary>
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>Minimum confidence required from AnalystsTeamNode to proceed.</summary>
    public double MinimumConfidence { get; set; } = 0.5;

    /// <summary>Minutes between full workflow runs.</summary>
    public int RunIntervalMinutes { get; set; } = 240;
}

/// <summary>
/// Cyclic graph executor for the trading pipeline.
/// <para>
/// <b>Entry phase:</b> AnalystsTeamNode → StrategyNode → RiskNode → ExecutionNode (with risk-rejection refinement loop).
/// </para>
/// <para>
/// <b>Monitor phase:</b> MonitorNode ↔ AnalystsTeamNode loop until the trade closes.
/// </para>
/// </summary>
public class TradingGraph
{
    private const string EndMarker = "__end__";

    private readonly IReadOnlyDictionary<string, INode> _nodes;
    private readonly IStateStore _stateStore;
    private readonly IResettableExecutor _resetter;
    private readonly GraphRunPublisher _publisher;
    private readonly TradingGraphSettings _settings;
    private readonly IActivityLogger _activityLogger;
    private readonly ILogger<TradingGraph> _logger;

    public TradingGraph(
        IReadOnlyDictionary<string, INode> nodes,
        IStateStore stateStore,
        IResettableExecutor resetter,
        GraphRunPublisher publisher,
        IOptions<TradingGraphSettings> settings,
        IActivityLogger activityLogger,
        ILogger<TradingGraph> logger)
    {
        _nodes = nodes;
        _stateStore = stateStore;
        _resetter = resetter;
        _publisher = publisher;
        _settings = settings.Value;
        _activityLogger = activityLogger;
        _logger = logger;
    }

    /// <summary>
    /// Run the full graph for a single symbol. Attempts checkpoint resume first.
    /// </summary>
    public async Task<TradingGraphState> RunAsync(string symbol, CancellationToken cancellationToken)
    {
        var state = TradingGraphState.Initial(symbol);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = "trading-pack",
            HoundId = "trading-graph",
            HoundName = "TradingGraph",
            Message = $"Graph run started for {symbol} (RunId: {state.RunId})",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var nextNodeId = Route(state);

            if (nextNodeId == EndMarker)
            {
                state = state with { IsComplete = true };
                await _stateStore.ClearAsync(state.RunId, cancellationToken);
                break;
            }

            if (!_nodes.TryGetValue(nextNodeId, out var node))
            {
                state = state with
                {
                    IsComplete = true,
                    ErrorMessage = $"Unknown node: {nextNodeId}",
                };
                break;
            }

            _logger.LogInformation("Graph [{RunId}] transitioning to {Node} (phase={Phase})",
                state.RunId, nextNodeId, state.Phase);

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = "trading-pack",
                HoundId = "trading-graph",
                HoundName = "TradingGraph",
                Message = $"→ {nextNodeId} (phase={state.Phase}, refinements={state.RefinementCount}, monitorCycles={state.MonitorCycleCount})",
                Severity = ActivitySeverity.Info,
                Metadata = new Dictionary<string, object>
                {
                    ["runId"] = state.RunId,
                    ["node"] = nextNodeId,
                    ["phase"] = state.Phase.ToString(),
                },
            }, cancellationToken);

            state = state with { CurrentNode = nextNodeId };

            // Transition phase when entering the monitor loop
            if (nextNodeId == "monitor-node" && state.Phase == GraphPhase.Entry)
            {
                state = state with { Phase = GraphPhase.Monitor };
            }

            await _stateStore.SaveAsync(state, cancellationToken);
            await _publisher.PublishAsync(state, cancellationToken);

            try
            {
                state = await node.ExecuteAsync(state, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Graph [{RunId}] node {Node} failed", state.RunId, nextNodeId);
                state = state with
                {
                    IsComplete = true,
                    ErrorMessage = $"Node {nextNodeId} failed: {ex.Message}",
                };
                await _stateStore.SaveAsync(state, cancellationToken);
                await _publisher.PublishAsync(state, cancellationToken);
                break;
            }
        }

        var severity = state.ErrorMessage is not null ? ActivitySeverity.Error : ActivitySeverity.Success;
        await _publisher.PublishAsync(state with { IsComplete = true }, cancellationToken);
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = "trading-pack",
            HoundId = "trading-graph",
            HoundName = "TradingGraph",
            Message = state.ErrorMessage ?? $"Graph run complete for {symbol}",
            Severity = severity,
            Metadata = new Dictionary<string, object>
            {
                ["runId"] = state.RunId,
                ["refinements"] = state.RefinementCount,
                ["monitorCycles"] = state.MonitorCycleCount,
            },
        }, cancellationToken);

        return state;
    }

    /// <summary>
    /// Resume an incomplete graph run from a checkpoint.
    /// </summary>
    public async Task<TradingGraphState?> ResumeAsync(string runId, CancellationToken cancellationToken)
    {
        var state = await _stateStore.LoadAsync(runId, cancellationToken);
        if (state is null || state.IsComplete)
            return state;

        _logger.LogInformation("Resuming graph run {RunId} from node {Node}", runId, state.CurrentNode);

        while (!cancellationToken.IsCancellationRequested)
        {
            var nextNodeId = Route(state);

            if (nextNodeId == EndMarker)
            {
                state = state with { IsComplete = true };
                await _stateStore.ClearAsync(state.RunId, cancellationToken);
                break;
            }

            if (!_nodes.TryGetValue(nextNodeId, out var node))
            {
                state = state with
                {
                    IsComplete = true,
                    ErrorMessage = $"Unknown node: {nextNodeId}",
                };
                break;
            }

            state = state with { CurrentNode = nextNodeId };
            await _stateStore.SaveAsync(state, cancellationToken);

            try
            {
                state = await node.ExecuteAsync(state, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Graph [{RunId}] node {Node} failed on resume", state.RunId, nextNodeId);
                state = state with
                {
                    IsComplete = true,
                    ErrorMessage = $"Node {nextNodeId} failed: {ex.Message}",
                };
                await _stateStore.SaveAsync(state, cancellationToken);
                break;
            }
        }

        return state;
    }

    /// <summary>
    /// Determines the next node based on the current state and phase.
    /// This encodes the full cyclic graph topology.
    /// </summary>
    internal string Route(TradingGraphState state)
    {
        return (state.Phase, state.CurrentNode) switch
        {
            // ── Entry phase ──────────────────────────────────────────────────
            (GraphPhase.Entry, null) => "analysts-team-node",
            (GraphPhase.Entry, "analysts-team-node") => ShouldSkipLowConfidence(state)
                ? EndMarker
                : "strategy-node",
            (GraphPhase.Entry, "strategy-node") => ShouldSkipHold(state)
                ? EndMarker
                : "risk-node",
            (GraphPhase.Entry, "risk-node") => RouteAfterRisk(state),
            (GraphPhase.Entry, "execution-node") => "monitor-node",

            // ── Monitor phase ────────────────────────────────────────────────
            (GraphPhase.Monitor, "monitor-node") => RouteAfterMonitor(state),
            (GraphPhase.Monitor, "analysts-team-node") => "monitor-node",

            _ => EndMarker,
        };
    }

    private string RouteAfterRisk(TradingGraphState state)
    {
        if (state.RiskOutput is null)
            return EndMarker;

        if (state.RiskOutput.Verdict != RiskVerdict.Rejected)
            return "execution-node";

        // Risk rejected — can we refine?
        if (state.RefinementCount < _settings.MaxRefinements)
            return "strategy-node";

        // Max refinements exceeded
        return EndMarker;
    }

    private string RouteAfterMonitor(TradingGraphState state)
    {
        if (state.MonitorOutput is null || !state.MonitorOutput.TradeOpen)
            return EndMarker;

        // Trade still open — delay, reset KV caches, and loop back to data
        // The actual delay and reset happen in the monitor node transition
        return "analysts-team-node";
    }

    private bool ShouldSkipLowConfidence(TradingGraphState state)
    {
        return state.DataOutput is not null
            && state.DataOutput.ConfidenceScore < _settings.MinimumConfidence;
    }

    private bool ShouldSkipHold(TradingGraphState state)
    {
        return state.StrategyOutput is not null
            && state.StrategyOutput.Action == TradeAction.Hold;
    }
}
