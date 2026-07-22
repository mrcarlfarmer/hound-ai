using Hound.Trading.Nodes;
using Hound.Trading.Nodes.Analysts;

namespace Hound.Trading.Tests.Nodes;

/// <summary>
/// Unit tests for <see cref="AnalystsTeamNode"/> helper methods,
/// particularly the data-derived confidence computation used as an
/// external validation measure against the LLM confidence score.
/// </summary>
[TestClass]
public sealed class AnalystsTeamNodeTests
{
    [TestMethod]
    public void ComputeDataDerivedConfidence_ReturnsNull_WhenNoPriceData()
    {
        var result = PreflightMetricsCalculator.ComputeDataDerivedConfidence(null, null, "Bullish");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ComputeDataDerivedConfidence_ReturnsBaseline_WhenNeutralTrendAverageVolume()
    {
        // Neutral trend with average volume (1.0) → capped at 0.55
        var result = PreflightMetricsCalculator.ComputeDataDerivedConfidence(100m, 1.0m, "Neutral");
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Value <= 0.55, $"Neutral trend should cap at 0.55, got {result.Value}");
        Assert.IsTrue(result.Value >= 0.05);
    }

    [TestMethod]
    public void ComputeDataDerivedConfidence_HigherForBullishWithStrongVolume()
    {
        // Bullish with high volume → should be near the top end
        var result = PreflightMetricsCalculator.ComputeDataDerivedConfidence(150m, 1.5m, "Bullish");
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Value >= 0.75, $"Bullish with strong volume should be >= 0.75, got {result.Value}");
    }

    [TestMethod]
    public void ComputeDataDerivedConfidence_HigherForBearishWithStrongVolume()
    {
        // Bearish with high volume → same directional logic as bullish
        var result = PreflightMetricsCalculator.ComputeDataDerivedConfidence(50m, 1.8m, "Bearish");
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Value >= 0.75, $"Bearish with strong volume should be >= 0.75, got {result.Value}");
    }

    [TestMethod]
    public void ComputeDataDerivedConfidence_LowerForDirectionalWithWeakVolume()
    {
        // Bullish with very low volume → less confidence
        var result = PreflightMetricsCalculator.ComputeDataDerivedConfidence(200m, 0.5m, "Bullish");
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Value < 0.75, $"Directional with weak volume should be < 0.75, got {result.Value}");
    }

    [TestMethod]
    public void ComputeDataDerivedConfidence_ClampsToValidRange()
    {
        // Extreme volume values should not push score outside [0.05, 1.0]
        var highResult = PreflightMetricsCalculator.ComputeDataDerivedConfidence(100m, 10.0m, "Bullish");
        Assert.IsNotNull(highResult);
        Assert.IsTrue(highResult!.Value <= 1.0, $"Score should not exceed 1.0, got {highResult.Value}");

        var lowResult = PreflightMetricsCalculator.ComputeDataDerivedConfidence(100m, 0.01m, "Neutral");
        Assert.IsNotNull(lowResult);
        Assert.IsTrue(lowResult!.Value >= 0.05, $"Score should not go below 0.05, got {lowResult.Value}");
    }

    [TestMethod]
    public void ComputeDataDerivedConfidence_NullVolume_UsesBaselineOnly()
    {
        // No volume data available, but price exists → should still work
        var result = PreflightMetricsCalculator.ComputeDataDerivedConfidence(100m, null, "Bullish");
        Assert.IsNotNull(result);
        // Base 0.5 + directional 0.15 = 0.65 (no volume boost)
        Assert.AreEqual(0.65, result!.Value, 0.01);
    }
}
