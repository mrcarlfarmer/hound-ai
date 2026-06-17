using Hound.Core.Models;

namespace Hound.Core.Tests.Models;

[TestClass]
public sealed class HoundConfigsTests
{
    [TestMethod]
    public void BaseHoundConfig_Defaults_AreInitialized()
    {
        var config = new BaseHoundConfig();

        Assert.AreEqual("qwen3.5:9b", config.Model);
        Assert.AreEqual(string.Empty, config.Instructions);
        Assert.AreEqual(512, config.MaxTokens);
        Assert.AreEqual(0.1, config.Temperature);
    }

    [TestMethod]
    public void AnalysisHoundConfig_Defaults_AreInitialized()
    {
        var config = new AnalysisHoundConfig();

        Assert.AreEqual(7, config.DataWindowDays);
        Assert.AreEqual(0, config.IndicatorWeights.Count);
        Assert.AreEqual(0.5, config.ConfidenceThreshold);
    }

    [TestMethod]
    public void StrategyHoundConfig_Defaults_AreInitialized()
    {
        var config = new StrategyHoundConfig();

        Assert.AreEqual(0, config.Indicators.Count);
        Assert.AreEqual(0, config.Timeframes.Count);
        Assert.AreEqual(0.7, config.BullishConfidenceThreshold);
        Assert.AreEqual(0.7, config.BearishConfidenceThreshold);
        Assert.AreEqual(0.7, config.EntryThreshold);
        Assert.AreEqual(0.6, config.ExitThreshold);
    }

    [TestMethod]
    public void RiskHoundConfig_Defaults_AreInitialized()
    {
        var config = new RiskHoundConfig();

        Assert.AreEqual(0.20, config.MaxPositionPct);
        Assert.AreEqual(0.80, config.MaxExposurePct);
        Assert.AreEqual(1000, config.MaxSharesPerOrder);
        Assert.AreEqual(0.10, config.MaxDrawdownPct);
        Assert.AreEqual(0.80, config.PortfolioExposureLimit);
    }

    [TestMethod]
    public void ExecutionHoundConfig_Defaults_AreInitialized()
    {
        var config = new ExecutionHoundConfig();

        Assert.AreEqual("Market", config.OrderType);
        Assert.AreEqual(0.001, config.SlippageTolerance);
        Assert.AreEqual("Day", config.TimeInForce);
    }

    [TestMethod]
    public void ConfigProperties_WhenSet_ReflectValues()
    {
        var config = new StrategyHoundConfig
        {
            Model = "qwen3",
            Instructions = "Follow the strategy plan.",
            MaxTokens = 1024,
            Temperature = 0.2,
            Indicators = ["RSI", "MACD"],
            Timeframes = ["1d"],
            EntryThreshold = 0.8
        };

        Assert.AreEqual("qwen3", config.Model);
        Assert.AreEqual("Follow the strategy plan.", config.Instructions);
        Assert.AreEqual(1024, config.MaxTokens);
        Assert.AreEqual(0.2, config.Temperature);
        Assert.AreEqual(2, config.Indicators.Count);
        Assert.AreEqual(1, config.Timeframes.Count);
        Assert.AreEqual(0.8, config.EntryThreshold);
    }
}
