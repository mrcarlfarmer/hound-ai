using Hound.Trading.Nodes;

namespace Hound.Trading.Tests.Nodes;

/// <summary>
/// Unit tests for <see cref="StrategyNode"/> sizing helpers.
/// These focus on the regression where the suggested per-position cap
/// rounded down to zero whole shares — which caused the model to emit
/// <c>quantity: 0</c> and the guard to coerce strong-Buy decisions into
/// Hold. The helper now returns fractional share counts so small accounts
/// can still take a sub-share position.
/// </summary>
[TestClass]
public sealed class StrategyNodeTests
{
    [TestMethod]
    public void CalculateSuggestedCap_WhenDollarCapBelowOneShare_ReturnsFractionalShares()
    {
        // Equity $200 -> 20% cap = $40. Price $200/share -> 0.2 shares fractional.
        // Previously this rounded to 0 whole shares and silently produced Hold.
        var (dollarCap, maxShares) = StrategyNode.CalculateSuggestedCap(
            price: 200m, equity: 200m, buyingPower: 1000m);

        Assert.AreEqual(40m, dollarCap, "Dollar cap should be 20% of equity ($40) when it is the binding constraint.");
        Assert.IsNotNull(maxShares, "Max shares must be populated for a positive dollar cap, even when sub-share.");
        Assert.AreEqual(0.2m, maxShares!.Value, "Fractional share count should be cap/price rounded to 4dp.");
    }

    [TestMethod]
    public void CalculateSuggestedCap_PrefersTheSmallerOfBuyingPowerAndEquityCaps()
    {
        // Buying power 30% = $30; equity 20% = $200. Min is $30 -> 0.3 shares at $100/share.
        var (dollarCap, maxShares) = StrategyNode.CalculateSuggestedCap(
            price: 100m, equity: 1000m, buyingPower: 100m);

        Assert.AreEqual(30m, dollarCap);
        Assert.AreEqual(0.3m, maxShares);
    }

    [TestMethod]
    public void CalculateSuggestedCap_RoundsSharesDownToFourDecimalPlaces()
    {
        // $100 cap at $300/share -> 0.333333... should round DOWN to 0.3333
        // so the resulting notional never exceeds the dollar cap.
        var (_, maxShares) = StrategyNode.CalculateSuggestedCap(
            price: 300m, equity: 500m, buyingPower: 1_000m);

        Assert.IsNotNull(maxShares);
        Assert.AreEqual(0.3333m, maxShares!.Value);
        Assert.IsTrue(maxShares.Value * 300m <= 100m, "Suggested notional must not exceed the dollar cap.");
    }

    [TestMethod]
    public void CalculateSuggestedCap_WithEnoughBuyingPower_ReturnsMultipleWholeShares()
    {
        // Buying power $10k -> 30% cap = $3000 -> 30 shares at $100.
        var (dollarCap, maxShares) = StrategyNode.CalculateSuggestedCap(
            price: 100m, equity: 100_000m, buyingPower: 10_000m);

        Assert.AreEqual(3_000m, dollarCap);
        Assert.AreEqual(30m, maxShares);
    }

    [TestMethod]
    public void CalculateSuggestedCap_WithNoAccountContext_ReturnsNulls()
    {
        var (dollarCap, maxShares) = StrategyNode.CalculateSuggestedCap(
            price: 50m, equity: null, buyingPower: null);

        Assert.IsNull(dollarCap);
        Assert.IsNull(maxShares);
    }

    [TestMethod]
    public void CalculateSuggestedCap_WithNonPositivePrice_ReturnsNulls()
    {
        var (dollarCap, maxShares) = StrategyNode.CalculateSuggestedCap(
            price: 0m, equity: 1_000m, buyingPower: 1_000m);

        Assert.IsNull(dollarCap);
        Assert.IsNull(maxShares);
    }
}
