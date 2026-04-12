using Hound.Core.LlmClient;
using Hound.Core.Logging;
using Hound.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent that autonomously proposes and evaluates incremental improvements to hound configurations.
/// Implements the autoresearch pattern: propose → eval → score → log for human review.
/// Does NOT auto-apply changes; sets status to PendingReview when an improvement is found.
/// </summary>
public class TunerHound
{
    private const string HoundId = "tuner-hound";
    private const string PackId = "trading-pack";
    private const string TunerDatabase = "hound-trading-pack";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly ChatClientAgent _agent;
    private readonly IDocumentStore _documentStore;
    private readonly IActivityLogger _activityLogger;
    private readonly IOllamaClientFactory _ollamaClientFactory;
    private readonly string _configDir;
    private readonly TunerConstraints _constraints;
    private readonly ILogger<TunerHound>? _logger;

    private static readonly string[] SupportedHounds =
        ["StrategyHound", "RiskHound", "AnalysisHound", "ExecutionHound"];

    public TunerHound(
        IChatClient chatClient,
        IDocumentStore documentStore,
        IActivityLogger activityLogger,
        IOllamaClientFactory ollamaClientFactory,
        string configDir,
        TunerConstraints constraints,
        ILoggerFactory? loggerFactory = null)
    {
        _documentStore = documentStore;
        _activityLogger = activityLogger;
        _ollamaClientFactory = ollamaClientFactory;
        _configDir = configDir;
        _constraints = constraints;
        _logger = loggerFactory?.CreateLogger<TunerHound>();

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are TunerHound, an autonomous configuration tuning agent.
                Your task is to propose ONE small, targeted improvement to a hound's configuration.
                You will be given the current configuration as JSON, a list of fields you are allowed to modify,
                and recent experiment history.
                
                Respond STRICTLY in JSON with this shape:
                {
                  "field": "<field name to change>",
                  "currentValue": <current value as JSON>,
                  "proposedValue": <new value as JSON>,
                  "rationale": "<explanation of why this change should improve performance>"
                }
                
                Rules:
                - Modify exactly ONE field.
                - Only modify fields in the allowedFields list.
                - Make conservative, incremental adjustments (e.g., nudge a threshold by 0.05, not 0.5).
                - Do not propose a change that was already tried and rated 'worse' in recent experiments.
                - Do not modify the 'Model' field.
                """,
            name: "TunerHound",
            description: "Proposes targeted improvements to hound configurations",
            loggerFactory: loggerFactory);
    }

    /// <summary>
    /// Runs one full experiment cycle: picks a hound, proposes a change, evals baseline and candidate,
    /// then logs a TunerExperiment to RavenDB.
    /// </summary>
    public async Task<TunerExperiment> RunExperimentAsync(
        string? houndName = null,
        CancellationToken cancellationToken = default)
    {
        houndName ??= PickNextHound();

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = HoundId,
            HoundName = "TunerHound",
            Message = $"Starting tuning experiment for {houndName}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var configPath = Path.Combine(_configDir, $"{houndName}.json");
        if (!File.Exists(configPath))
        {
            _logger?.LogWarning("Config file not found for {HoundName}: {Path}", houndName, configPath);
            return CreateCrashExperiment(houndName, $"Config file not found: {configPath}");
        }

        var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
        var allowedFields = _constraints.GetAllowedFields(houndName);
        var recentExperiments = await GetRecentExperimentsAsync(houndName, cancellationToken);

        TunerProposal proposal;
        try
        {
            proposal = await ProposeModificationAsync(
                houndName, configJson, allowedFields, recentExperiments, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "TunerHound failed to propose modification for {HoundName}", houndName);
            return CreateCrashExperiment(houndName, $"Proposal failed: {ex.Message}");
        }

        var candidateJson = ApplyModification(configJson, proposal);
        if (candidateJson is null)
        {
            return CreateCrashExperiment(houndName, $"Failed to apply modification to field '{proposal.Field}'");
        }

        var baselineScore = ScoreConfig(houndName, configJson);
        var candidateScore = ScoreConfig(houndName, candidateJson);
        var delta = candidateScore - baselineScore;

        var status = delta > 0.001
            ? TunerExperimentStatus.PendingReview
            : delta < -0.001
                ? TunerExperimentStatus.Worse
                : TunerExperimentStatus.Equal;

        var experiment = new TunerExperiment
        {
            HoundName = houndName,
            Timestamp = DateTime.UtcNow,
            ConfigBefore = configJson,
            ConfigAfter = candidateJson,
            BaselineScore = baselineScore,
            CandidateScore = candidateScore,
            Status = status,
            Rationale = proposal.Rationale,
        };

        await SaveExperimentAsync(experiment, cancellationToken);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = HoundId,
            HoundName = "TunerHound",
            Message = $"Experiment complete for {houndName}: {status} (Δ={delta:+0.000;-0.000;0.000}) — {proposal.Rationale}",
            Severity = status == TunerExperimentStatus.PendingReview ? ActivitySeverity.Success : ActivitySeverity.Info,
        }, cancellationToken);

        return experiment;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<TunerProposal> ProposeModificationAsync(
        string houndName,
        string currentConfigJson,
        IReadOnlyList<string> allowedFields,
        IReadOnlyList<TunerExperiment> recentExperiments,
        CancellationToken cancellationToken)
    {
        var historyLines = recentExperiments
            .Select(e => $"  field={ExtractFieldFromRationale(e.Rationale)}, status={e.Status}")
            .ToList();

        var prompt = $"""
            Hound: {houndName}
            Current config:
            {currentConfigJson}
            
            Allowed fields to modify: {string.Join(", ", allowedFields)}
            
            Recent experiment history (last {recentExperiments.Count}):
            {(historyLines.Count == 0 ? "  (no history)" : string.Join("\n", historyLines))}
            
            Propose ONE targeted modification to improve this hound's performance.
            """;

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            session,
            cancellationToken: cancellationToken);

        var json = response.Text ?? "{}";

        var proposal = JsonSerializer.Deserialize<TunerProposal>(json, JsonOptions)
            ?? throw new InvalidOperationException("TunerHound returned null or unparseable proposal");

        if (string.IsNullOrWhiteSpace(proposal.Field))
            throw new InvalidOperationException("TunerHound proposal has no field name");

        if (!allowedFields.Contains(proposal.Field, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"TunerHound proposed modifying disallowed field '{proposal.Field}'");

        return proposal;
    }

    /// <summary>
    /// Applies a single field modification to a JSON config string.
    /// Returns null if the field is not found or the modification cannot be applied.
    /// </summary>
    private static string? ApplyModification(string configJson, TunerProposal proposal)
    {
        try
        {
            var node = JsonNode.Parse(configJson);
            if (node is not JsonObject obj) return null;

            var key = obj.FirstOrDefault(kv =>
                string.Equals(kv.Key, proposal.Field, StringComparison.OrdinalIgnoreCase)).Key;

            if (key is null) return null;

            obj[key] = proposal.ProposedValue?.DeepClone();
            return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Heuristic scoring of a config based on parameter safety and quality ranges.
    /// Returns a score in [0, 1]; higher is better.
    /// </summary>
    private static double ScoreConfig(string houndName, string configJson)
    {
        try
        {
            return houndName switch
            {
                "StrategyHound" => ScoreStrategyConfig(configJson),
                "RiskHound" => ScoreRiskConfig(configJson),
                "AnalysisHound" => ScoreAnalysisConfig(configJson),
                "ExecutionHound" => ScoreExecutionConfig(configJson),
                _ => 0.5,
            };
        }
        catch
        {
            return 0.0;
        }
    }

    private static double ScoreStrategyConfig(string configJson)
    {
        var config = JsonSerializer.Deserialize<StrategyConfigSnapshot>(configJson, JsonOptions);
        if (config is null) return 0.0;

        double score = 1.0;

        // Temperature: lower is more consistent for trading
        score -= Math.Max(0, config.Temperature - 0.3) * 0.5;

        // Thresholds: should be in [0.5, 0.95]
        if (config.BullishConfidenceThreshold is < 0.5 or > 0.95) score -= 0.2;
        if (config.BearishConfidenceThreshold is < 0.5 or > 0.95) score -= 0.2;
        if (config.EntryThreshold is < 0.5 or > 0.95) score -= 0.1;
        if (config.ExitThreshold is < 0.4 or > 0.95) score -= 0.1;

        return Math.Max(0.0, Math.Min(1.0, score));
    }

    private static double ScoreRiskConfig(string configJson)
    {
        var config = JsonSerializer.Deserialize<RiskConfigSnapshot>(configJson, JsonOptions);
        if (config is null) return 0.0;

        double score = 1.0;

        // Conservative limits score higher
        score -= Math.Max(0, config.MaxPositionPct - 0.25) * 1.0;
        score -= Math.Max(0, config.MaxDrawdownPct - 0.15) * 1.0;
        if (config.MaxSharesPerOrder > 1000) score -= 0.2;
        if (config.Temperature > 0.3) score -= 0.1;

        return Math.Max(0.0, Math.Min(1.0, score));
    }

    private static double ScoreAnalysisConfig(string configJson)
    {
        var config = JsonSerializer.Deserialize<AnalysisConfigSnapshot>(configJson, JsonOptions);
        if (config is null) return 0.0;

        double score = 1.0;

        // More data window is better, up to a point
        if (config.DataWindowDays is < 3 or > 30) score -= 0.2;
        if (config.ConfidenceThreshold is < 0.3 or > 0.9) score -= 0.2;
        if (config.Temperature > 0.4) score -= 0.1;

        return Math.Max(0.0, Math.Min(1.0, score));
    }

    private static double ScoreExecutionConfig(string configJson)
    {
        var config = JsonSerializer.Deserialize<ExecutionConfigSnapshot>(configJson, JsonOptions);
        if (config is null) return 0.0;

        double score = 1.0;

        // Slippage: lower is better, but 0 may indicate no tolerance for fills
        if (config.SlippageTolerance > 0.01) score -= 0.3;
        if (config.Temperature > 0.2) score -= 0.2;

        return Math.Max(0.0, Math.Min(1.0, score));
    }

    private async Task<IReadOnlyList<TunerExperiment>> GetRecentExperimentsAsync(
        string houndName,
        CancellationToken cancellationToken,
        int count = 10)
    {
        try
        {
            using var session = _documentStore.OpenAsyncSession(TunerDatabase);
            return await session.Query<TunerExperiment>()
                .Where(e => e.HoundName == houndName)
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not load recent experiments for {HoundName}", houndName);
            return [];
        }
    }

    private async Task SaveExperimentAsync(
        TunerExperiment experiment,
        CancellationToken cancellationToken)
    {
        using var session = _documentStore.OpenAsyncSession(TunerDatabase);
        await session.StoreAsync(experiment, cancellationToken);
        await session.SaveChangesAsync(cancellationToken);
    }

    private string PickNextHound()
    {
        // Round-robin through supported hounds
        var idx = (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute) % SupportedHounds.Length;
        return SupportedHounds[idx];
    }

    private static TunerExperiment CreateCrashExperiment(string houndName, string reason) =>
        new()
        {
            HoundName = houndName,
            Timestamp = DateTime.UtcNow,
            ConfigBefore = string.Empty,
            ConfigAfter = string.Empty,
            BaselineScore = 0,
            CandidateScore = 0,
            Status = TunerExperimentStatus.Crash,
            Rationale = reason,
        };

    private static string ExtractFieldFromRationale(string rationale)
    {
        // Best-effort: pull a field name from existing rationale text
        if (string.IsNullOrWhiteSpace(rationale)) return "(unknown)";
        var words = rationale.Split(' ');
        return words.Length > 0 ? words[0] : "(unknown)";
    }

    // ── Snapshot types for config scoring (no inheritance needed) ────────────

    private sealed class StrategyConfigSnapshot
    {
        public double Temperature { get; set; }
        public double BullishConfidenceThreshold { get; set; } = 0.7;
        public double BearishConfidenceThreshold { get; set; } = 0.7;
        public double EntryThreshold { get; set; } = 0.7;
        public double ExitThreshold { get; set; } = 0.6;
    }

    private sealed class RiskConfigSnapshot
    {
        public double Temperature { get; set; }
        public double MaxPositionPct { get; set; } = 0.20;
        public double MaxDrawdownPct { get; set; } = 0.10;
        public int MaxSharesPerOrder { get; set; } = 1000;
    }

    private sealed class AnalysisConfigSnapshot
    {
        public double Temperature { get; set; }
        public int DataWindowDays { get; set; } = 7;
        public double ConfidenceThreshold { get; set; } = 0.5;
    }

    private sealed class ExecutionConfigSnapshot
    {
        public double Temperature { get; set; }
        public double SlippageTolerance { get; set; } = 0.001;
    }
}

/// <summary>
/// A proposed modification from TunerHound — one field, its current value, and a proposed new value.
/// </summary>
public sealed class TunerProposal
{
    public string Field { get; set; } = string.Empty;
    public JsonNode? CurrentValue { get; set; }
    public JsonNode? ProposedValue { get; set; }
    public string Rationale { get; set; } = string.Empty;
}
