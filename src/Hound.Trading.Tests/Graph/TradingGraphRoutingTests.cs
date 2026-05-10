using Hound.Core.Logging;
using Hound.Trading.Graph;
using Hound.Trading.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Hound.Trading.Tests.Graph;

[TestClass]
public sealed class TradingGraphRoutingTests
{
    private TradingGraph CreateGraph(int maxRefinements = 2)
    {
        var nodes = new Dictionary<string, INode>();
        var stateStore = new Mock<IStateStore>();
        var resetter = new Mock<IResettableExecutor>();
        var settings = Options.Create(new TradingGraphSettings { MaxRefinements = maxRefinements });
        var activityLogger = new Mock<IActivityLogger>();
        var logger = new Mock<ILogger<TradingGraph>>();

        return new TradingGraph(nodes, stateStore.Object, resetter.Object, settings,
            activityLogger.Object, logger.Object);
    }

    [TestMethod]
    public void Route_EntryPhase_NullNode_ReturnsDataNode()
    {
        var graph = CreateGraph();
        var state = TradingGraphState.Initial("AAPL");

        var next = graph.Route(state);

        Assert.AreEqual("data-node", next);
    }

    [TestMethod]
    public void Route_EntryPhase_DataNode_ReturnsStrategyNode()
    {
        var graph = CreateGraph();
        var state = TradingGraphState.Initial("AAPL") with
        {
            CurrentNode = "data-node",
            DataOutput = new MarketAnalysis("AAPL", 150, 0.05m, "Bullish", 0.8, "Strong uptrend"),
        };

        var next = graph.Route(state);

        Assert.AreEqual("strategy-node", next);
    }

    [TestMethod]
    public void Route_EntryPhase_DataNode_LowConfidence_ReturnsEnd()
    {
        var graph = CreateGraph();
        var state = TradingGraphState.Initial("AAPL") with
        {
            CurrentNode = "data-node",
            DataOutput = new MarketAnalysis("AAPL", 150, 0.01m, "Neutral", 0.3, "Weak signal"),
        };

        var next = graph.Route(state);

        Assert.AreEqual("__end__", next);
    }

    [TestMethod]
    public void Route_EntryPhase_StrategyNode_ReturnsRiskNode()
    {
        var graph = CreateGraph();
        var state = TradingGraphState.Initial("AAPL") with
        {
            CurrentNode = "strategy-node",
            StrategyOutput = new TradingDecision("AAPL", TradeAction.Buy, 10, "Bullish", 0.8),
        };

        var next = graph.Route(state);

        Assert.AreEqual("risk-node", next);
    }

    [TestMethod]
    public void Route_EntryPhase_StrategyNode_Hold_ReturnsEnd()
    {
        var graph = CreateGraph();
        var state = TradingGraphState.Initial("AAPL") with
        {
            CurrentNode = "strategy-node",
            StrategyOutput = new TradingDecision("AAPL", TradeAction.Hold, 0, "Neutral", 0.5),
        };

        var next = graph.Route(state);

        Assert.AreEqual("__end__", next);
    }

    [TestMethod]
    public void Route_EntryPhase_RiskNode_Approved_ReturnsExecutionNode()
    {
        var graph = CreateGraph();
        var decision = new TradingDecision("AAPL", TradeAction.Buy, 10, "Bullish", 0.8);
        var state = TradingGraphState.Initial("AAPL") with
        {
            CurrentNode = "risk-node",
            RiskOutput = new RiskAssessment(RiskVerdict.Approved, decision, "Within limits"),
        };

        var next = graph.Route(state);

        Assert.AreEqual("execution-node", next);
    }

    [TestMethod]
    public void Route_EntryPhase_RiskNode_Rejected_BelowMax_ReturnsStrategyNode()
    {
        var graph = CreateGraph(maxRefinements: 2);
        var decision = new TradingDecision("AAPL", TradeAction.Buy, 100, "Bullish", 0.8);
        var state = TradingGraphState.Initial("AAPL") with
        {
            CurrentNode = "risk-node",
            RefinementCount = 1,
            RiskOutput = new RiskAssessment(RiskVerdict.Rejected, decision, "Too large"),
        };

        var next = graph.Route(state);

        Assert.AreEqual("strategy-node", next);
    }

    [TestMethod]
    public void Route_EntryPhase_RiskNode_Rejected_AtMax_ReturnsEnd()
    {
        var graph = CreateGraph(maxRefinements: 2);
        var decision = new TradingDecision("AAPL", TradeAction.Buy, 100, "Bullish", 0.8);
        var state = TradingGraphState.Initial("AAPL") with
        {
            CurrentNode = "risk-node",
            RefinementCount = 2,
            RiskOutput = new RiskAssessment(RiskVerdict.Rejected, decision, "Still too large"),
        };

        var next = graph.Route(state);

        Assert.AreEqual("__end__", next);
    }

    [TestMethod]
    public void Route_EntryPhase_ExecutionNode_ReturnsMonitorNode()
    {
        var graph = CreateGraph();
        var state = TradingGraphState.Initial("AAPL") with
        {
            CurrentNode = "execution-node",
        };

        var next = graph.Route(state);

        Assert.AreEqual("monitor-node", next);
    }

    [TestMethod]
    public void Route_MonitorPhase_MonitorNode_TradeOpen_ReturnsDataNode()
    {
        var graph = CreateGraph();
        var state = TradingGraphState.Initial("AAPL") with
        {
            Phase = GraphPhase.Monitor,
            CurrentNode = "monitor-node",
            MonitorOutput = new MonitorResult(true, Hound.Core.Models.FillStatus.Filled, 150m, 2.5m, "Position held"),
        };

        var next = graph.Route(state);

        Assert.AreEqual("data-node", next);
    }

    [TestMethod]
    public void Route_MonitorPhase_MonitorNode_TradeClosed_ReturnsEnd()
    {
        var graph = CreateGraph();
        var state = TradingGraphState.Initial("AAPL") with
        {
            Phase = GraphPhase.Monitor,
            CurrentNode = "monitor-node",
            MonitorOutput = new MonitorResult(false, Hound.Core.Models.FillStatus.Filled, null, null, "Position closed"),
        };

        var next = graph.Route(state);

        Assert.AreEqual("__end__", next);
    }

    [TestMethod]
    public void Route_MonitorPhase_DataNode_ReturnsMonitorNode()
    {
        var graph = CreateGraph();
        var state = TradingGraphState.Initial("AAPL") with
        {
            Phase = GraphPhase.Monitor,
            CurrentNode = "data-node",
        };

        var next = graph.Route(state);

        Assert.AreEqual("monitor-node", next);
    }
}
