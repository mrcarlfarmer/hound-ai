using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.Hounds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Trading.Workflows;

public class TradingWorkflowSettings
{
    public const string SectionName = "TradingWorkflow";

    /// <summary>Symbols to analyse each run.</summary>
    public List<string> Symbols { get; set; } = ["AAPL", "MSFT", "SPY"];

    /// <summary>When true, workflow pauses before execution and requires human approval.</summary>
    public bool HumanInTheLoop { get; set; } = false;

    /// <summary>Minimum confidence required to proceed from analysis.</summary>
    public double MinimumConfidence { get; set; } = 0.5;

    /// <summary>Cron expression for scheduled execution (used for documentation; actual interval is controlled by <see cref="RunIntervalMinutes"/>).</summary>
    public string CronSchedule { get; set; } = "0 */4 * * 1-5";

    /// <summary>Minutes between workflow runs. Defaults to 240 (4 hours).</summary>
    public int RunIntervalMinutes { get; set; } = 240;
}

/// <summary>
/// AF graph-based workflow: AnalysisHound → StrategyHound → RiskHound → ExecutionHound.
/// Includes optional human-in-the-loop checkpoint before execution.
/// </summary>
public class TradingWorkflow
{
    private readonly AnalysisHound _analysisHound;
    private readonly StrategyHound _strategyHound;
    private readonly RiskHound _riskHound;
    private readonly ExecutionHound _executionHound;
    private readonly IActivityLogger _activityLogger;
    private readonly TradingWorkflowSettings _settings;
    private readonly ILogger<TradingWorkflow> _logger;

    public TradingWorkflow(
        AnalysisHound analysisHound,
        StrategyHound strategyHound,
        RiskHound riskHound,
        ExecutionHound executionHound,
        IActivityLogger activityLogger,
        IOptions<TradingWorkflowSettings> settings,
        ILogger<TradingWorkflow> logger)
    {
        _analysisHound = analysisHound;
        _strategyHound = strategyHound;
        _riskHound = riskHound;
        _executionHound = executionHound;
        _activityLogger = activityLogger;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("TradingWorkflow starting for symbols: {Symbols}",
            string.Join(", ", _settings.Symbols));

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = "trading-pack",
            HoundId = "trading-workflow",
            HoundName = "TradingWorkflow",
            Message = $"Workflow run started for: {string.Join(", ", _settings.Symbols)}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        foreach (var symbol in _settings.Symbols)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await ProcessSymbolAsync(symbol, cancellationToken);
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = "trading-pack",
            HoundId = "trading-workflow",
            HoundName = "TradingWorkflow",
            Message = "Workflow run complete",
            Severity = ActivitySeverity.Success,
        }, cancellationToken);
    }

    private async Task ProcessSymbolAsync(string symbol, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing symbol {Symbol}", symbol);

        // Step 1: Analysis
        var analysis = await _analysisHound.AnalyseAsync(symbol, cancellationToken);

        if (analysis.ConfidenceScore < _settings.MinimumConfidence)
        {
            _logger.LogInformation("Skipping {Symbol} — confidence {Score:P0} below threshold {Threshold:P0}",
                symbol, analysis.ConfidenceScore, _settings.MinimumConfidence);
            return;
        }

        // Step 2: Strategy
        var decision = await _strategyHound.DecideAsync(analysis, cancellationToken);

        if (decision.Action == TradeAction.Hold)
        {
            _logger.LogInformation("Holding {Symbol} — strategy says Hold", symbol);
            return;
        }

        // Step 3: Risk evaluation
        var assessment = await _riskHound.EvaluateAsync(decision, cancellationToken);

        // Optional: Human-in-the-loop checkpoint before execution
        if (_settings.HumanInTheLoop && assessment.Verdict != RiskVerdict.Rejected)
        {
            _logger.LogWarning(
                "HITL: Awaiting human approval for {Action} {Quantity} {Symbol}. Set HumanInTheLoop=false to auto-execute.",
                decision.Action, assessment.AdjustedQuantity ?? decision.Quantity, symbol);

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = "trading-pack",
                HoundId = "trading-workflow",
                HoundName = "TradingWorkflow",
                Message = $"HITL checkpoint: {decision.Action} {assessment.AdjustedQuantity ?? decision.Quantity} {symbol} — awaiting approval",
                Severity = ActivitySeverity.Warning,
            }, cancellationToken);
            return;
        }

        // Step 4: Execution
        var result = await _executionHound.ExecuteAsync(assessment, cancellationToken);

        _logger.LogInformation("Execution result for {Symbol}: {Success} — {Message}",
            symbol, result.Success, result.Message);
    }
}
