using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.Graph;
using Hound.Trading.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using System.Net.Http.Json;

namespace Hound.Trading;

/// <summary>
/// Background service that runs the TradingGraph on a configurable schedule
/// and processes on-demand run requests from the dashboard.
/// </summary>
public class TradingWorker : BackgroundService
{
    private const string PackId = "trading-pack";
    private const string Database = "hound-trading-pack";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly TradingGraph _graph;
    private readonly TradingGraphSettings _settings;
    private readonly IStateStore _stateStore;
    private readonly IDocumentStore _documentStore;
    private readonly GraphRunPublisher _publisher;
    private readonly IActivityLogger _activityLogger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiBaseUrl;
    private readonly ILogger<TradingWorker> _logger;

    /// <summary>Tracks the last time each monitor-phase run was advanced, keyed by RunId.</summary>
    private readonly Dictionary<string, DateTimeOffset> _lastMonitorCycle = new();

    public TradingWorker(
        TradingGraph graph,
        IOptions<TradingGraphSettings> settings,
        IStateStore stateStore,
        IDocumentStore documentStore,
        GraphRunPublisher publisher,
        IActivityLogger activityLogger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TradingWorker> logger)
    {
        _graph = graph;
        _settings = settings.Value;
        _stateStore = stateStore;
        _documentStore = documentStore;
        _publisher = publisher;
        _activityLogger = activityLogger;
        _httpClientFactory = httpClientFactory;
        _apiBaseUrl = (configuration["HoundApi:BaseUrl"] ?? "http://hound-api:8080").TrimEnd('/');
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RegisterPackAsync(stoppingToken);
        await ResumeIncompleteRunsAsync(stoppingToken);

        _logger.LogInformation("TradingWorker starting. Poll interval: {Poll}s, scheduled interval: {Interval} min",
            PollInterval.TotalSeconds, _settings.RunIntervalMinutes);

        var nextScheduledRun = DateTimeOffset.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Process any pending on-demand requests
                await ProcessPendingRequestsAsync(stoppingToken);

                // Apply any human approval decisions written by the API
                await ApplyPendingApprovalsAsync(stoppingToken);

                // Advance monitor-phase runs that are due for their next cycle
                await AdvanceMonitorRunsAsync(stoppingToken);

                // Run scheduled symbols if due
                if (_settings.Symbols.Count > 0 && DateTimeOffset.UtcNow >= nextScheduledRun)
                {
                    foreach (var symbol in _settings.Symbols)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        await _graph.RunAsync(symbol, stoppingToken);
                    }
                    nextScheduledRun = DateTimeOffset.UtcNow.AddMinutes(_settings.RunIntervalMinutes);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TradingWorker encountered an error");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("TradingWorker stopped");
    }

    private async Task ProcessPendingRequestsAsync(CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(Database);
        var pending = await session.Query<RunRequest>()
            .Where(r => r.Status == RunRequestStatus.Pending)
            .OrderBy(r => r.RequestedAt)
            .Take(1)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0) return;

        var request = pending[0];
        _logger.LogInformation("Processing run request {Id} for {Symbol}", request.Id, request.Symbol);

        request.Status = RunRequestStatus.Running;
        request.StartedAt = DateTime.UtcNow;
        await session.SaveChangesAsync(cancellationToken);

        try
        {
            var state = await _graph.RunAsync(
                request.Symbol,
                cancellationToken,
                onRunIdAssigned: async (runId, ct) =>
                {
                    // Link the request to the GraphRun as soon as the runId
                    // exists so the dashboard can dedupe the "Waiting for
                    // worker..." card against the live GraphRun entry.
                    request.RunId = runId;
                    await session.SaveChangesAsync(ct);
                });
            request.RunId = state.RunId;

            if (state.IsComplete)
            {
                request.Status = state.ErrorMessage is not null
                    ? RunRequestStatus.Failed
                    : RunRequestStatus.Completed;
                request.ErrorMessage = state.ErrorMessage;
                request.CompletedAt = DateTime.UtcNow;
            }
            else
            {
                // Run yielded in monitor phase — mark request completed
                // (the monitor loop continues via AdvanceMonitorRunsAsync)
                request.Status = RunRequestStatus.Completed;
                request.CompletedAt = DateTime.UtcNow;
                _lastMonitorCycle[state.RunId] = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run request {Id} failed", request.Id);
            request.Status = RunRequestStatus.Failed;
            request.ErrorMessage = ex.Message;
            request.CompletedAt = DateTime.UtcNow;
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Picks up any <see cref="GraphApproval"/> documents written by the API
    /// (Approve/Reject in the dashboard), applies them to the corresponding
    /// <see cref="TradingGraphState"/> checkpoint, and either resumes the run
    /// (on approve) or finalises it with a rejection result (on reject).
    /// The approval document is deleted once applied.
    /// </summary>
    private async Task ApplyPendingApprovalsAsync(CancellationToken cancellationToken)
    {
        List<GraphApproval> pending;
        using (var read = _documentStore.OpenAsyncSession(Database))
        {
            pending = await read.Query<GraphApproval>()
                .OrderBy(a => a.DecidedAt)
                .Take(10)
                .ToListAsync(cancellationToken);
        }

        foreach (var approval in pending)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                await ApplySingleApprovalAsync(approval, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to apply approval for run {RunId}", approval.RunId);
            }
        }
    }

    private async Task ApplySingleApprovalAsync(GraphApproval approval, CancellationToken cancellationToken)
    {
        var checkpoint = await _stateStore.LoadAsync(approval.RunId, cancellationToken);
        if (checkpoint is null)
        {
            _logger.LogWarning(
                "Approval received for unknown or completed run {RunId} \u2014 discarding", approval.RunId);
            await DeleteApprovalAsync(approval.Id, cancellationToken);
            return;
        }

        if (checkpoint.ApprovalStatus != ApprovalStatus.Pending)
        {
            _logger.LogWarning(
                "Approval received for run {RunId} not in Pending state (current: {Status}) \u2014 discarding",
                approval.RunId, checkpoint.ApprovalStatus);
            await DeleteApprovalAsync(approval.Id, cancellationToken);
            return;
        }

        var updated = checkpoint with
        {
            ApprovalStatus = approval.Decision,
            ApprovalDecidedBy = approval.DecidedBy,
            ApprovalDecidedAt = approval.DecidedAt,
            ApprovalNotes = approval.Notes,
        };

        if (approval.Decision == ApprovalStatus.Rejected)
        {
            var decision = updated.RiskOutput?.Decision;
            var note = string.IsNullOrWhiteSpace(approval.Notes)
                ? "Rejected by human reviewer"
                : approval.Notes!;

            updated = updated with
            {
                ExecutionOutput = new ExecutionResult(
                    false,
                    decision?.Symbol ?? updated.Symbol,
                    decision?.Action ?? TradeAction.Hold,
                    decision?.Quantity ?? 0m,
                    null,
                    string.Empty,
                    $"Rejected by {approval.DecidedBy ?? "user"}: {note}"),
                IsComplete = true,
            };

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = "approval-node",
                HoundName = "ApprovalNode",
                Message = $"Trade rejected by {approval.DecidedBy ?? "user"} for {updated.Symbol}: {note}",
                Severity = ActivitySeverity.Warning,
                Metadata = new Dictionary<string, object>
                {
                    ["runId"] = updated.RunId,
                },
            }, cancellationToken);

            await _stateStore.SaveAsync(updated, cancellationToken);
            await _publisher.PublishAsync(updated, cancellationToken);
            await _stateStore.ClearAsync(updated.RunId, cancellationToken);
            await DeleteApprovalAsync(approval.Id, cancellationToken);
            return;
        }

        if (approval.Decision == ApprovalStatus.Approved)
        {
            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = "approval-node",
                HoundName = "ApprovalNode",
                Message = $"Trade approved by {approval.DecidedBy ?? "user"} for {updated.Symbol}"
                    + (string.IsNullOrWhiteSpace(approval.Notes) ? "" : $" \u2014 {approval.Notes}"),
                Severity = ActivitySeverity.Success,
                Metadata = new Dictionary<string, object>
                {
                    ["runId"] = updated.RunId,
                },
            }, cancellationToken);

            await _stateStore.SaveAsync(updated, cancellationToken);
            await _publisher.PublishAsync(updated, cancellationToken);
            await DeleteApprovalAsync(approval.Id, cancellationToken);

            // Resume the graph \u2014 the router will now bypass approval-node
            // and proceed to execution-node.
            await _graph.ResumeAsync(updated.RunId, cancellationToken);
            return;
        }

        // Decision was NotRequested or Pending \u2014 nothing to do, drop the doc.
        _logger.LogWarning(
            "Ignoring no-op approval for run {RunId} (decision: {Decision})",
            approval.RunId, approval.Decision);
        await DeleteApprovalAsync(approval.Id, cancellationToken);
    }

    private async Task DeleteApprovalAsync(string approvalId, CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(Database);
        session.Delete(approvalId);
        await session.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Advances at most ONE monitor-phase run that is due for its next cycle.
    /// Only one is advanced per poll iteration so the loop returns to
    /// <see cref="ProcessPendingRequestsAsync"/> promptly, ensuring queued
    /// requests are not starved by long-running monitor LLM calls.
    /// </summary>
    private async Task AdvanceMonitorRunsAsync(CancellationToken cancellationToken)
    {
        var monitorDelay = TimeSpan.FromSeconds(_settings.MonitorDelaySeconds);
        IReadOnlyList<TradingGraphState> incomplete;

        try
        {
            incomplete = await _stateStore.ListIncompleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query incomplete runs for monitor advancement");
            return;
        }

        foreach (var checkpoint in incomplete)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Only advance runs that are in monitor phase
            if (checkpoint.Phase != GraphPhase.Monitor) continue;

            // Respect the delay between monitor cycles
            if (_lastMonitorCycle.TryGetValue(checkpoint.RunId, out var lastCycle)
                && DateTimeOffset.UtcNow - lastCycle < monitorDelay)
            {
                continue;
            }

            _logger.LogInformation("Advancing monitor cycle for {RunId} ({Symbol})",
                checkpoint.RunId, checkpoint.Symbol);

            try
            {
                var state = await _graph.ResumeAsync(checkpoint.RunId, cancellationToken);
                if (state is null || state.IsComplete)
                {
                    _lastMonitorCycle.Remove(checkpoint.RunId);
                }
                else
                {
                    _lastMonitorCycle[checkpoint.RunId] = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to advance monitor run {RunId}", checkpoint.RunId);
                _lastMonitorCycle[checkpoint.RunId] = DateTimeOffset.UtcNow;
            }

            // Only advance one run per poll cycle to avoid starving pending requests
            break;
        }
    }

    /// <summary>
    /// On startup, resume any graph runs that were in-flight when the process
    /// last shut down (e.g., a monitor loop waiting for a pre-market order to fill).
    /// </summary>
    private async Task ResumeIncompleteRunsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TradingGraphState> incomplete;
        try
        {
            incomplete = await _stateStore.ListIncompleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query incomplete graph runs for resume");
            return;
        }

        if (incomplete.Count == 0) return;

        _logger.LogInformation("Found {Count} incomplete graph run(s) to resume", incomplete.Count);

        foreach (var checkpoint in incomplete)
        {
            if (cancellationToken.IsCancellationRequested) break;

            _logger.LogInformation("Resuming run {RunId} for {Symbol} (node: {Node}, phase: {Phase})",
                checkpoint.RunId, checkpoint.Symbol, checkpoint.CurrentNode, checkpoint.Phase);

            // Monitor-phase runs are handled by AdvanceMonitorRunsAsync on
            // the regular poll cycle. Seed the tracker so they run immediately
            // on the first iteration.
            if (checkpoint.Phase == GraphPhase.Monitor)
            {
                _lastMonitorCycle[checkpoint.RunId] = DateTimeOffset.MinValue;
                continue;
            }

            try
            {
                var state = await _graph.ResumeAsync(checkpoint.RunId, cancellationToken);
                if (state is not null && !state.IsComplete)
                {
                    _lastMonitorCycle[state.RunId] = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume run {RunId}", checkpoint.RunId);
            }
        }
    }

    private async Task RegisterPackAsync(CancellationToken cancellationToken)
    {
        var registration = new PackRegistration
        {
            Id = PackId,
            Name = "Trading Pack",
            Hounds =
            [
                new() { Id = "analysts-team-node", Name = "AnalystsTeam" },
                new() { Id = "strategy-node", Name = "StrategyNode" },
                new() { Id = "risk-node", Name = "RiskNode" },
                new() { Id = "approval-node", Name = "ApprovalNode" },
                new() { Id = "execution-node", Name = "ExecutionNode" },
                new() { Id = "monitor-node", Name = "MonitorNode" },
            ]
        };

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.PostAsJsonAsync(
                    $"{_apiBaseUrl}/api/packs/register", registration, cancellationToken);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("Pack '{PackId}' registered with API", PackId);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Pack registration attempt {Attempt}/10 failed, retrying...", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), cancellationToken);
            }
        }

        _logger.LogError("Failed to register pack '{PackId}' after 10 attempts", PackId);
    }
}
