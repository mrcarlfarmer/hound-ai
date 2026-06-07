using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Hound.Trading.Nodes.Analysts;

namespace Hound.Trading.Nodes;

/// <summary>
/// Analyst team node inspired by TradingAgents. Orchestrates four specialist
/// analysts — Market, Fundamentals, News, Sentiment — sequentially, then
/// hands their reports to the synthesiser to produce a single
/// <see cref="MarketAnalysis"/>.
/// </summary>
/// <remarks>
/// The analyst and synthesiser <em>agents</em> are owned by the dedicated
/// classes in <c>Hound.Trading.Nodes.Analysts</c>. This node is responsible
/// only for ordering them, layering deterministic pre-flight market metrics
/// over the LLM output, and reporting confidence divergence.
/// </remarks>
public class AnalystsTeamNode : INode
{
    public string NodeId => "analysts-team-node";
    public string PackId => "trading-pack";

    // Per-call wall-clock budgets. Ollama's OpenAI-compat layer does not
    // reliably honour max_tokens, so a long repetition loop can run for many
    // minutes. These timeouts cancel the HTTP call, which causes Ollama to
    // abort generation, and let the graph continue with a degraded but
    // non-blocking result.
    private static readonly TimeSpan AnalystTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan SynthesiserTimeout = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<AnalystBase> _analysts;
    private readonly AnalystSynthesiser _synthesiser;
    private readonly IAlpacaService _alpacaService;
    private readonly IActivityLogger _activityLogger;

    public AnalystsTeamNode(
        MarketAnalyst marketAnalyst,
        FundamentalsAnalyst fundamentalsAnalyst,
        NewsAnalyst newsAnalyst,
        SentimentAnalyst sentimentAnalyst,
        AnalystSynthesiser synthesiser,
        IAlpacaService alpacaService,
        IActivityLogger activityLogger)
    {
        // Order matters: the dashboard renders analyst progress in this order,
        // and the synthesis prompt lists the sections in the same sequence.
        _analysts =
        [
            marketAnalyst,
            fundamentalsAnalyst,
            newsAnalyst,
            sentimentAnalyst,
        ];
        _synthesiser = synthesiser;
        _alpacaService = alpacaService;
        _activityLogger = activityLogger;
    }

    public async Task<TradingGraphState> ExecuteAsync(
        TradingGraphState state, CancellationToken cancellationToken)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "AnalystsTeam",
            Message = $"Analyst team starting analysis of {state.Symbol}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        // Pre-flight: confirm the broker actually has bar data for this symbol.
        // If not, skip the (expensive, slow) analyst pipeline and emit a clean
        // low-confidence MarketAnalysis so the graph can terminate the run via
        // the existing minimum-confidence routing rule.
        var preflight = await PreflightMetricsCalculator.ComputeAsync(
            _alpacaService, state.Symbol, cancellationToken);

        // Resolve the canonical company name so we can disambiguate similar
        // tickers in the analyst prompts (e.g. ROK → Rockwell Automation vs
        // ROKU → Roku Inc). Without this the LLM mis-recalls obscure tickers
        // from memory and hallucinates reports about the wrong company.
        var asset = await _alpacaService.GetAssetAsync(state.Symbol, cancellationToken);
        var companyName = asset?.Name;
        var symbolLabel = string.IsNullOrWhiteSpace(companyName)
            ? state.Symbol
            : $"{state.Symbol} ({companyName})";

        if (preflight.LastPrice is null)
        {
            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = NodeId,
                HoundName = "AnalystsTeam",
                Message = $"No market data available for {symbolLabel}; skipping analyst pipeline.",
                Severity = ActivitySeverity.Warning,
            }, cancellationToken);

            var noData = new MarketAnalysis(
                state.Symbol,
                LastPrice: null,
                VolumeChange: null,
                Trend: "Neutral",
                ConfidenceScore: 0d,
                Summary: $"No market data available for {symbolLabel} from the broker. Skipping analysis.",
                CompanyName: companyName);
            return state with { DataOutput = noData };
        }

        var priceLine = preflight.LastPrice is decimal lp
            ? $"Current price: ${lp:F2} (authoritative — anchor all price levels to this)."
            : "Current price: unavailable.";

        // Per-analyst user prompts. Order must match `_analysts`.
        var prompts = new[]
        {
            $"Analyse the stock {symbolLabel} for the past 7 trading days. Today is {DateTime.UtcNow:yyyy-MM-dd}. {priceLine}",
            $"Analyse the fundamentals for {symbolLabel}. {priceLine}",
            $"Analyse recent news and market trends for {symbolLabel}. {priceLine}",
            $"Analyse social media sentiment and public opinion for {symbolLabel}. {priceLine}",
        };

        // Sequential by design — the single-GPU Ollama instance serialises
        // requests anyway, so parallel execution would only fragment the
        // dashboard activity feed without improving wall-clock time.
        // Switching to `Task.WhenAll` is a one-line change when multi-worker
        // inference becomes available.
        var reports = new string[_analysts.Count];
        for (int i = 0; i < _analysts.Count; i++)
        {
            reports[i] = await _analysts[i].AnalyseAsync(
                state.Symbol, prompts[i], AnalystTimeout, cancellationToken);
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "AnalystsTeam",
            Message = $"All analysts complete for {state.Symbol}, synthesising...",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var synthesisPrompt = $"""
            Symbol: {state.Symbol}

            ## Market Report
            {reports[0]}

            ## Fundamentals Report
            {reports[1]}

            ## News Report
            {reports[2]}

            ## Sentiment Report
            {reports[3]}

            Now output ONLY the JSON object with symbol, lastPrice, volumeChange, trend, confidenceScore, and summary. No other text.
            """;

        var analysis = await _synthesiser.SynthesiseAsync(
            state.Symbol, synthesisPrompt, SynthesiserTimeout, cancellationToken);

        // Attach individual reports + the pre-flight market metrics (override
        // LLM-supplied lastPrice/volumeChange because the broker numbers are
        // authoritative). ATR(14) and key support/resistance levels are pure
        // deterministic data — never asked of the LLM — so they're attached
        // here too for the downstream strategy hound to select from.
        analysis = analysis with
        {
            LastPrice = preflight.LastPrice ?? analysis.LastPrice,
            VolumeChange = preflight.VolumeChange ?? analysis.VolumeChange,
            Atr14 = preflight.Atr14,
            KeyLevels = preflight.KeyLevels,
            MarketReport = reports[0],
            FundamentalsReport = reports[1],
            NewsReport = reports[2],
            SentimentReport = reports[3],
            CompanyName = companyName,
        };

        // External validation: compute a deterministic confidence score from
        // market data to compare against the LLM-derived value. Large
        // divergence between the two signals a potential hallucination or
        // anchoring bias in the model.
        var dataConfidence = PreflightMetricsCalculator.ComputeDataDerivedConfidence(
            preflight.LastPrice, preflight.VolumeChange, analysis.Trend);

        var llmConfidence = analysis.ConfidenceScore;
        var divergence = llmConfidence.HasValue && dataConfidence.HasValue
            ? Math.Abs(llmConfidence.Value - dataConfidence.Value)
            : (double?)null;

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = "AnalystsTeam",
            Message = $"Analysis complete for {state.Symbol}: {analysis.Trend} " +
                      $"(LLM confidence {llmConfidence?.ToString("P0") ?? "N/A"}, " +
                      $"data-derived confidence {dataConfidence?.ToString("P0") ?? "N/A"}, " +
                      $"divergence {divergence?.ToString("P0") ?? "N/A"})",
            Severity = ActivitySeverity.Success,
        }, cancellationToken);

        return state with { DataOutput = analysis };
    }
}
