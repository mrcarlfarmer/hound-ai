using Alpaca.Markets;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;

namespace Hound.Trading.Nodes.Analysts;

/// <summary>
/// Pure technical-analysis helpers used by <see cref="AnalystsTeamNode"/>
/// before the LLM pipeline runs. All methods are deterministic — they touch
/// only broker bar data, never an LLM — so the values they produce are
/// treated as authoritative downstream (overriding any LLM-supplied numbers).
/// </summary>
public static class PreflightMetricsCalculator
{
    /// <summary>
    /// Small immutable view over an OHLCV bar so the technical helpers can be
    /// unit-tested without mocking the full Alpaca <see cref="IBar"/> surface
    /// area. Volume is decimal to mirror the broker SDK.
    /// </summary>
    public record BarSnapshot(decimal High, decimal Low, decimal Close, decimal Volume, DateTime Time);

    /// <summary>
    /// Result of the broker-data pre-flight: deterministic numbers that the
    /// downstream analysts and synthesiser can rely on as authoritative.
    /// </summary>
    public record PreflightMetrics(
        decimal? LastPrice,
        decimal? VolumeChange,
        decimal? Atr14,
        KeyLevels? KeyLevels,
        IReadOnlyList<ChartBar>? Bars = null,
        DateTime? BarsFrom = null,
        DateTime? BarsTo = null)
    {
        public static readonly PreflightMetrics Empty = new(null, null, null, null);
    }

    /// <summary>
    /// Daily-bar lookback used by <see cref="ComputeAsync"/>. Sized to match
    /// the dashboard's default 90-day chart window so the persisted
    /// <see cref="ChartSnapshot"/> covers the full default view without a
    /// second Alpaca round-trip.
    /// </summary>
    public const int LookbackDays = 90;

    /// <summary>
    /// Deterministically derives last price, relative volume change, ATR(14),
    /// and key support/resistance levels from Alpaca daily bars. Returns an
    /// all-null result if data is unavailable. Also returns the full OHLCV
    /// bars so callers can persist them for the dashboard's Chart tab.
    /// </summary>
    public static async Task<PreflightMetrics> ComputeAsync(
        IAlpacaService alpacaService, string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var to = DateTime.UtcNow.Date;
            var from = to.AddDays(-LookbackDays);
            var bars = await alpacaService.GetBarsAsync(symbol, from, to, BarTimeFrame.Day, cancellationToken);
            if (bars.Count == 0)
                return PreflightMetrics.Empty;

            var ordered = bars
                .OrderBy(b => b.TimeUtc)
                .Select(b => new BarSnapshot(b.High, b.Low, b.Close, b.Volume, b.TimeUtc))
                .ToList();

            // Full OHLCV captured separately because BarSnapshot intentionally
            // omits Open (only used by the static technical helpers). Persisted
            // on the GraphRun so reviewers can rebuild the analysts' chart.
            var chartBars = bars
                .OrderBy(b => b.TimeUtc)
                .Select(b => new ChartBar(b.TimeUtc, b.Open, b.High, b.Low, b.Close, b.Volume))
                .ToList();

            var lastBar = ordered[^1];
            decimal lastPrice = lastBar.Close;

            decimal? volumeChange = null;
            if (ordered.Count >= 2)
            {
                var priorBars = ordered.Take(ordered.Count - 1).TakeLast(20).ToList();
                if (priorBars.Count > 0)
                {
                    var avgPrior = priorBars.Average(b => b.Volume);
                    if (avgPrior > 0)
                        volumeChange = Math.Round(lastBar.Volume / avgPrior, 2);
                }
            }

            var atr14 = CalculateAtr14(ordered);
            var keyLevels = CalculateKeyLevels(ordered, lastPrice);

            return new PreflightMetrics(lastPrice, volumeChange, atr14, keyLevels, chartBars, from, to);
        }
        catch
        {
            return PreflightMetrics.Empty;
        }
    }

    /// <summary>
    /// Computes 14-period Average True Range using a simple mean of the last
    /// 14 true ranges (close enough to Wilder's smoothing for sizing decisions
    /// and far easier to reason about). Returns <c>null</c> when there are
    /// fewer than 15 bars (14 TR values require a prior close).
    /// </summary>
    public static decimal? CalculateAtr14(IReadOnlyList<BarSnapshot> bars)
    {
        if (bars.Count < 15) return null;

        var trs = new List<decimal>(bars.Count - 1);
        for (int i = 1; i < bars.Count; i++)
        {
            var prevClose = bars[i - 1].Close;
            var hl = bars[i].High - bars[i].Low;
            var hc = Math.Abs(bars[i].High - prevClose);
            var lc = Math.Abs(bars[i].Low - prevClose);
            trs.Add(Math.Max(hl, Math.Max(hc, lc)));
        }

        var last14 = trs.TakeLast(14).ToList();
        if (last14.Count < 14) return null;
        return Math.Round(last14.Average(), 2);
    }

    /// <summary>
    /// Extracts a concise menu of support/resistance levels from the bar
    /// history. Combines the 20-day high/low with classic prior-day pivot
    /// levels (R1/R2/S1/S2), keeps only values within ±25% of
    /// <paramref name="currentPrice"/>, dedupes near-duplicates (within 0.5%),
    /// and partitions them into support (≤ current) and resistance (≥ current),
    /// both sorted ascending and rounded to 2dp.
    /// </summary>
    public static KeyLevels? CalculateKeyLevels(IReadOnlyList<BarSnapshot> bars, decimal currentPrice)
    {
        if (bars.Count == 0 || currentPrice <= 0) return null;

        var candidates = new List<decimal>();

        // 20-day range (or whatever's available).
        var window = bars.TakeLast(20).ToList();
        if (window.Count > 0)
        {
            candidates.Add(window.Max(b => b.High));
            candidates.Add(window.Min(b => b.Low));
        }

        // Classic pivot levels derived from the most recent completed bar.
        var pivotBar = bars[^1];
        var pp = (pivotBar.High + pivotBar.Low + pivotBar.Close) / 3m;
        var range = pivotBar.High - pivotBar.Low;
        candidates.Add(2m * pp - pivotBar.Low);   // R1
        candidates.Add(pp + range);                // R2
        candidates.Add(2m * pp - pivotBar.High);  // S1
        candidates.Add(pp - range);                // S2

        // Keep only levels within a ±25% band of current price; outside that
        // a "level" is more likely noise than something a swing trader will
        // act on.
        var lower = currentPrice * 0.75m;
        var upper = currentPrice * 1.25m;
        var filtered = candidates
            .Where(c => c >= lower && c <= upper && c > 0)
            .Select(c => Math.Round(c, 2))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        // Cluster: drop any level within 0.5% of one we've already kept.
        var clusterTolerance = currentPrice * 0.005m;
        var clustered = new List<decimal>();
        foreach (var c in filtered)
        {
            if (clustered.Count == 0 || c - clustered[^1] > clusterTolerance)
                clustered.Add(c);
        }

        var support = clustered.Where(c => c <= currentPrice).ToList();
        var resistance = clustered.Where(c => c >= currentPrice).ToList();

        if (support.Count == 0 && resistance.Count == 0) return null;
        return new KeyLevels(support, resistance);
    }

    /// <summary>
    /// Computes a deterministic confidence score from raw market data to serve as
    /// an external validation measure against the LLM-derived confidence. Uses
    /// price trend strength and volume deviation as objective signals.
    /// </summary>
    /// <remarks>
    /// This is intentionally simple — it measures whether the available market data
    /// supports a directional opinion. A high data-derived confidence with a low
    /// LLM confidence (or vice versa) suggests the model may be anchoring or
    /// hallucinating rather than reasoning from evidence.
    /// </remarks>
    public static double? ComputeDataDerivedConfidence(
        decimal? lastPrice, decimal? volumeChange, string trend)
    {
        if (lastPrice is null)
            return null;

        // Tuning constants for the data-derived confidence heuristic.
        // VolumeImpactMultiplier: how much a 1x volume deviation shifts confidence
        const double VolumeImpactMultiplier = 0.25;
        // MaxVolumeAdjustment: ceiling on absolute volume-based adjustment
        const double MaxVolumeAdjustment = 0.20;
        // DirectionalBoost: bonus for having a non-neutral trend direction
        const double DirectionalBoost = 0.15;
        // StrongVolumeThreshold: volume ratio above which an extra boost applies
        const decimal StrongVolumeThreshold = 1.2m;
        // StrongVolumeBonus: extra boost when volume exceeds StrongVolumeThreshold
        const double StrongVolumeBonus = 0.10;

        // Start at a neutral baseline
        double score = 0.5;

        // Volume signal: deviation from average adds/removes confidence
        if (volumeChange is decimal vc)
        {
            // vc > 1.0 means above-average volume (stronger signal)
            // vc < 1.0 means below-average volume (weaker signal)
            var volumeBoost = Math.Clamp(
                (double)(vc - 1.0m) * VolumeImpactMultiplier,
                -MaxVolumeAdjustment, MaxVolumeAdjustment);
            score += volumeBoost;
        }

        // Directional clarity: a non-neutral trend with supporting volume deserves
        // higher confidence than a neutral assessment
        if (trend is "Bullish" or "Bearish")
        {
            score += DirectionalBoost;
            // Strong volume on a directional move is more convincing
            if (volumeChange is > StrongVolumeThreshold)
                score += StrongVolumeBonus;
        }
        else
        {
            // Neutral trend — cap confidence since there's no clear direction
            score = Math.Min(score, 0.55);
        }

        return Math.Clamp(score, 0.05, 1.0);
    }
}
