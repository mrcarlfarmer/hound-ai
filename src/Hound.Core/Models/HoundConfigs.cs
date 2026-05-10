namespace Hound.Core.Models;

/// <summary>
/// Base configuration shared by all hounds.
/// </summary>
public class BaseHoundConfig
{
    public string Model { get; set; } = "gemma3";
    public string Instructions { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 512;
    public double Temperature { get; set; } = 0.1;
}

/// <summary>
/// Configuration for AnalysisHound — analysis parameters, indicator weights and confidence thresholds.
/// </summary>
public class AnalysisHoundConfig : BaseHoundConfig
{
    public int DataWindowDays { get; set; } = 7;
    public Dictionary<string, double> IndicatorWeights { get; set; } = new();
    public double ConfidenceThreshold { get; set; } = 0.5;
}

/// <summary>
/// Configuration for StrategyHound — indicators, timeframes, and entry/exit thresholds.
/// </summary>
public class StrategyHoundConfig : BaseHoundConfig
{
    public List<string> Indicators { get; set; } = [];
    public List<string> Timeframes { get; set; } = [];
    public double BullishConfidenceThreshold { get; set; } = 0.7;
    public double BearishConfidenceThreshold { get; set; } = 0.7;
    public double EntryThreshold { get; set; } = 0.7;
    public double ExitThreshold { get; set; } = 0.6;
}

/// <summary>
/// Configuration for RiskHound — position limits, drawdown caps, and portfolio exposure.
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
/// Configuration for ExecutionHound — order types, slippage tolerance, and order watcher settings.
/// </summary>
public class ExecutionHoundConfig : BaseHoundConfig
{
    public string OrderType { get; set; } = "Market";
    public double SlippageTolerance { get; set; } = 0.001;
    public string TimeInForce { get; set; } = "Day";

    /// <summary>Seconds between order status polls. Default: 5.</summary>
    public int OrderWatchIntervalSeconds { get; set; } = 5;

    /// <summary>Minutes before the watcher gives up on a pending order. Default: 30.</summary>
    public int OrderWatchTimeoutMinutes { get; set; } = 30;
}

/// <summary>
/// Tuner constraints specifying which config fields each hound is allowed to have modified.
/// </summary>
public class TunerConstraints
{
    public Dictionary<string, List<string>> AllowedModifications { get; set; } = new();

    public IReadOnlyList<string> GetAllowedFields(string houndName)
    {
        if (AllowedModifications.TryGetValue(houndName, out var fields))
            return fields.AsReadOnly();
        return [];
    }
}
