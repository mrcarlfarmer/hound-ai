namespace Hound.Core.Models;

/// <summary>
/// Base configuration shared by all hounds.
/// </summary>
public class BaseHoundConfig
{
    public string Model { get; set; } = "qwen3.5:9b";
    public string Instructions { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 512;
    public double Temperature { get; set; } = 0.1;
}

/// <summary>
/// Configuration for AnalystsTeamNode (formerly AnalysisHound) — analysis parameters, indicator weights and confidence thresholds.
/// </summary>
public class AnalysisHoundConfig : BaseHoundConfig
{
    public int DataWindowDays { get; set; } = 7;
    public Dictionary<string, double> IndicatorWeights { get; set; } = new();
    public double ConfidenceThreshold { get; set; } = 0.5;
}

/// <summary>
/// Configuration for StrategyNode — indicators, timeframes, entry/exit thresholds,
/// and bull-vs-bear debate parameters.
/// </summary>
public class StrategyHoundConfig : BaseHoundConfig
{
    public List<string> Indicators { get; set; } = [];
    public List<string> Timeframes { get; set; } = [];
    public double BullishConfidenceThreshold { get; set; } = 0.7;
    public double BearishConfidenceThreshold { get; set; } = 0.7;
    public double EntryThreshold { get; set; } = 0.7;
    public double ExitThreshold { get; set; } = 0.6;

    /// <summary>
    /// When <c>true</c>, StrategyNode runs a bull-vs-bear MAF group-chat debate
    /// before the coordinator agent produces the final <see cref="TradingDecision"/>.
    /// When <c>false</c>, StrategyNode falls back to its single-agent legacy path.
    /// </summary>
    public bool DebateEnabled { get; set; } = true;

    /// <summary>
    /// Number of debate turns each side (bull, bear) takes. Total debate messages
    /// = <c>DebateTurnsPerSide * 2</c>. Keep small to bound latency; 2 is a good
    /// balance between rebuttal depth and Ollama wall-clock budget.
    /// </summary>
    public int DebateTurnsPerSide { get; set; } = 2;
}

/// <summary>
/// Configuration for RiskNode — position limits, drawdown caps, and portfolio exposure.
/// </summary>
public class RiskHoundConfig : BaseHoundConfig
{
    public double MaxPositionPct { get; set; } = 0.20;
    public double MaxExposurePct { get; set; } = 0.80;
    public int MaxSharesPerOrder { get; set; } = 1000;
    public double MaxDrawdownPct { get; set; } = 0.10;
    public double PortfolioExposureLimit { get; set; } = 0.80;
}

/// <summary>
/// Configuration for ExecutionNode — order types and slippage tolerance.
/// </summary>
public class ExecutionHoundConfig : BaseHoundConfig
{
    public string OrderType { get; set; } = "Market";
    public double SlippageTolerance { get; set; } = 0.001;
    public string TimeInForce { get; set; } = "Day";
}

/// <summary>
/// Configuration for MonitorNode — trade lifecycle monitoring.
/// </summary>
public class MonitorNodeConfig : BaseHoundConfig
{
}

/// <summary>
/// Allowlist describing which JSON fields the Tuner is permitted to modify on
/// each hound's config. Used by <c>TunerController.ApplyExperiment</c> to
/// reject experiments that would mutate fields outside the allowlist (e.g.,
/// silently flipping <c>StrategyHound.DebateEnabled</c> to <c>false</c>).
/// Loaded from <c>Config/TunerConstraints.json</c>.
/// </summary>
public class TunerConstraints
{
    /// <summary>
    /// Map of hound name (e.g., <c>"StrategyHound"</c>) to the list of field
    /// names the Tuner may mutate on that hound's config document.
    /// </summary>
    public Dictionary<string, List<string>> AllowedModifications { get; set; } = new();

    /// <summary>
    /// Returns the allowlisted fields for <paramref name="houndName"/>, or an
    /// empty list when no allowlist is registered (meaning: nothing tunable).
    /// </summary>
    public IReadOnlyList<string> GetAllowedFields(string houndName) =>
        AllowedModifications.TryGetValue(houndName, out var fields)
            ? fields
            : Array.Empty<string>();
}
