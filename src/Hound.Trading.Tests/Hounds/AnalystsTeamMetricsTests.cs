using Hound.Trading.Nodes;

namespace Hound.Trading.Tests.Nodes;

/// <summary>
/// Unit tests for the deterministic technical helpers on
/// <see cref="AnalystsTeamNode"/> (ATR(14) and key support/resistance level
/// extraction). These run purely on synthetic bar data — no broker, no LLM —
/// so they pin down the contract that <see cref="MarketAnalysis.Atr14"/> and
/// <see cref="MarketAnalysis.KeyLevels"/> will rely on once the strategy
/// hound starts selecting entry/stop/target levels from them.
/// </summary>
[TestClass]
public sealed class AnalystsTeamMetricsTests
{
    [TestMethod]
    public void CalculateAtr14_FewerThan15Bars_ReturnsNull()
    {
        var bars = MakeBars(count: 10, baseClose: 100m);
        var atr = AnalystsTeamNode.CalculateAtr14(bars);
        Assert.IsNull(atr, "ATR requires 14 true ranges (so 15 bars). Anything less should refuse to compute.");
    }

    [TestMethod]
    public void CalculateAtr14_FlatBars_ReturnsExpectedRange()
    {
        // Every bar: H=101, L=99, C=100. TR = H-L = 2 (prevClose comparisons are also 2).
        // 14-period mean -> 2.
        var bars = Enumerable.Range(0, 20)
            .Select(i => new AnalystsTeamNode.BarSnapshot(
                High: 101m, Low: 99m, Close: 100m, Volume: 1_000m,
                Time: DateTime.UtcNow.Date.AddDays(-20 + i)))
            .ToList();

        var atr = AnalystsTeamNode.CalculateAtr14(bars);
        Assert.AreEqual(2m, atr);
    }

    [TestMethod]
    public void CalculateAtr14_WithGapUp_PicksUpExtendedTrueRange()
    {
        // Steady $1 daily range, then one bar that gaps from prevClose 100 up to
        // L=105, H=108. TR for that bar = max(3, |108-100|, |105-100|) = 8.
        var bars = new List<AnalystsTeamNode.BarSnapshot>();
        for (int i = 0; i < 14; i++)
        {
            bars.Add(new AnalystsTeamNode.BarSnapshot(100.5m, 99.5m, 100m, 1_000m,
                DateTime.UtcNow.Date.AddDays(-15 + i)));
        }
        bars.Add(new AnalystsTeamNode.BarSnapshot(108m, 105m, 107m, 1_000m, DateTime.UtcNow.Date));

        var atr = AnalystsTeamNode.CalculateAtr14(bars);

        Assert.IsNotNull(atr);
        // 13 prior TRs of 1.0 + one TR of 8.0 across the last 14 → mean ≈ 1.5.
        Assert.AreEqual(1.5m, atr!.Value, "Gap-up day must inflate ATR via the prev-close comparison.");
    }

    [TestMethod]
    public void CalculateKeyLevels_EmptyBars_ReturnsNull()
    {
        var levels = AnalystsTeamNode.CalculateKeyLevels(Array.Empty<AnalystsTeamNode.BarSnapshot>(), currentPrice: 100m);
        Assert.IsNull(levels);
    }

    [TestMethod]
    public void CalculateKeyLevels_ZeroCurrentPrice_ReturnsNull()
    {
        var bars = MakeBars(20, 100m);
        var levels = AnalystsTeamNode.CalculateKeyLevels(bars, currentPrice: 0m);
        Assert.IsNull(levels);
    }

    [TestMethod]
    public void CalculateKeyLevels_PartitionsAroundCurrentPrice()
    {
        // 20 bars oscillating 90..110, current price 100. Expect at least the
        // 20-day high (110) above and the 20-day low (90) below.
        var bars = Enumerable.Range(0, 20)
            .Select(i => new AnalystsTeamNode.BarSnapshot(
                High: 110m - i * 0.1m,
                Low: 90m + i * 0.1m,
                Close: 100m + (i % 2 == 0 ? 1m : -1m),
                Volume: 1_000m,
                Time: DateTime.UtcNow.Date.AddDays(-20 + i)))
            .ToList();

        var levels = AnalystsTeamNode.CalculateKeyLevels(bars, currentPrice: 100m);

        Assert.IsNotNull(levels);
        Assert.IsTrue(levels!.Support.All(s => s <= 100m), "Support entries must be ≤ current price.");
        Assert.IsTrue(levels.Resistance.All(r => r >= 100m), "Resistance entries must be ≥ current price.");
        Assert.IsTrue(levels.Support.SequenceEqual(levels.Support.OrderBy(s => s)), "Support must be sorted ascending.");
        Assert.IsTrue(levels.Resistance.SequenceEqual(levels.Resistance.OrderBy(r => r)), "Resistance must be sorted ascending.");
        Assert.IsTrue(levels.Support.Any(s => s <= 91m), "20-day low (~90) should appear in support.");
        Assert.IsTrue(levels.Resistance.Any(r => r >= 109m), "20-day high (~110) should appear in resistance.");
    }

    [TestMethod]
    public void CalculateKeyLevels_DropsLevelsFarFromCurrentPrice()
    {
        // One outlier bar with absurd high; current price $100 so anything > $125 is dropped.
        var bars = MakeBars(20, 100m).ToList();
        bars.Add(new AnalystsTeamNode.BarSnapshot(High: 500m, Low: 99m, Close: 100m, Volume: 1_000m, Time: DateTime.UtcNow.Date));

        var levels = AnalystsTeamNode.CalculateKeyLevels(bars, currentPrice: 100m);

        Assert.IsNotNull(levels);
        Assert.IsFalse(levels!.Resistance.Any(r => r > 125m), "Levels outside ±25% of current price should be filtered out.");
    }

    [TestMethod]
    public void CalculateKeyLevels_LevelsAreRoundedToTwoDecimals()
    {
        var bars = MakeBars(20, 100m);
        var levels = AnalystsTeamNode.CalculateKeyLevels(bars, currentPrice: 100m);

        Assert.IsNotNull(levels);
        foreach (var l in levels!.Support.Concat(levels.Resistance))
        {
            Assert.AreEqual(Math.Round(l, 2), l, $"Level {l} should be rounded to 2dp.");
        }
    }

    private static List<AnalystsTeamNode.BarSnapshot> MakeBars(int count, decimal baseClose)
    {
        var rng = new Random(42);
        return Enumerable.Range(0, count)
            .Select(i =>
            {
                var close = baseClose + (decimal)(rng.NextDouble() - 0.5);
                return new AnalystsTeamNode.BarSnapshot(
                    High: close + 0.5m,
                    Low: close - 0.5m,
                    Close: close,
                    Volume: 1_000m,
                    Time: DateTime.UtcNow.Date.AddDays(-count + i));
            })
            .ToList();
    }
}
