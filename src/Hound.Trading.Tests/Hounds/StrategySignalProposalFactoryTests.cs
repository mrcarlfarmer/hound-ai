using Hound.Trading.Hounds;

namespace Hound.Trading.Tests.Hounds;

[TestClass]
public sealed class StrategySignalProposalFactoryTests
{
    [TestMethod]
    public void CreateDecision_BullishHighConfidence_ReturnsBuyWithPositiveQuantity()
    {
        var worldState = new WorldState(
            "AAPL",
            200m,
            0.18m,
            "Bullish",
            0.82,
            "Momentum remains strong.");

        var decision = StrategySignalProposalFactory.CreateDecision(worldState);

        Assert.AreEqual("AAPL", decision.Symbol);
        Assert.AreEqual(TradeAction.Buy, decision.Action);
        Assert.IsTrue(decision.Quantity > 0);
        Assert.AreEqual(0.82, decision.Confidence);
        StringAssert.Contains(decision.Reasoning, "adding exposure");
    }

    [TestMethod]
    public void CreateProposal_HoldDecision_ReturnsNull()
    {
        var worldState = new WorldState(
            "MSFT",
            415m,
            0.02m,
            "Neutral",
            0.45,
            "Mixed momentum.");
        var holdDecision = new TradingDecision("MSFT", TradeAction.Hold, 0, "No edge.", 0.45);

        var proposal = StrategySignalProposalFactory.CreateProposal(worldState, holdDecision);

        Assert.IsNull(proposal);
    }

    [TestMethod]
    public void NormalizeDecision_MissingQuantity_UsesDeterministicFallbackQuantity()
    {
        var worldState = new WorldState(
            "TSLA",
            250m,
            -0.1m,
            "Bearish",
            0.74,
            "Trend is weakening.");
        var incompleteDecision = new TradingDecision("IGNORED", TradeAction.Sell, 0, "", 1.2);

        var decision = StrategySignalProposalFactory.NormalizeDecision(worldState, incompleteDecision);

        Assert.AreEqual("TSLA", decision.Symbol);
        Assert.AreEqual(TradeAction.Sell, decision.Action);
        Assert.IsTrue(decision.Quantity > 0);
        Assert.AreEqual(1.0, decision.Confidence);
        StringAssert.Contains(decision.Reasoning, "reducing exposure");
    }
}
