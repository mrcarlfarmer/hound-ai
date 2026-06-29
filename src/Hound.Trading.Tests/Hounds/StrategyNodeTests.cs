using Hound.Trading.Nodes;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;

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

    // ── Debate flow ───────────────────────────────────────────────────────────

    /// <summary>
    /// End-to-end happy path: with debate enabled, StrategyNode runs the
    /// MAF round-robin group chat for the configured number of turns,
    /// captures the transcript onto graph state, and lets the coordinator
    /// produce the final JSON decision.
    /// </summary>
    [TestMethod]
    public async Task Debate_RoundRobinProducesTurnsThenCoordinatorDecides()
    {
        var responses = new Queue<string>(
        [
            "BULL: Strong upward momentum, RSI 65, volume 2x average.",
            "BEAR: Resistance overhead at +5%, hold off until breakout confirms.",
            "BULL: Volume divergence supports breakout in this session.",
            "BEAR: Risk/reward unfavorable without a confirmed close above resistance.",
            "{\"symbol\":\"AAPL\",\"action\":\"Buy\",\"quantity\":1,\"reasoning\":\"Bull case prevails on momentum and volume.\",\"confidence\":0.8,\"trailPercent\":5}",
        ]);
        var node = BuildNodeWithChatResponses(responses, debateEnabled: true, turnsPerSide: 2);

        var state = TradingGraphState.Initial("AAPL") with
        {
            DataOutput = new MarketAnalysis(
                Symbol: "AAPL",
                LastPrice: 175m,
                VolumeChange: 2.0m,
                Trend: "Bullish",
                ConfidenceScore: 0.85,
                Summary: "Strong upward price momentum with above-average volume."),
        };

        var result = await node.ExecuteAsync(state, CancellationToken.None);

        Assert.IsNotNull(result.StrategyOutput);
        Assert.AreEqual(TradeAction.Buy, result.StrategyOutput!.Action);
        Assert.IsNotNull(result.StrategyDebate);
        Assert.AreEqual(4, result.StrategyDebate!.Count, "Two turns per side should yield 4 debate messages.");
        Assert.AreEqual("Bull", result.StrategyDebate[0].Role);
        Assert.AreEqual("Bear", result.StrategyDebate[1].Role);
        Assert.AreEqual("Bull", result.StrategyDebate[2].Role);
        Assert.AreEqual("Bear", result.StrategyDebate[3].Role);
    }

    [TestMethod]
    public async Task Debate_DisabledViaConfig_UsesSingleAgentPathAndProducesNoTranscript()
    {
        var responses = new Queue<string>(
        [
            "{\"symbol\":\"AAPL\",\"action\":\"Hold\",\"quantity\":0,\"reasoning\":\"Insufficient signal.\",\"confidence\":0.3}",
        ]);
        var node = BuildNodeWithChatResponses(responses, debateEnabled: false, turnsPerSide: 2);

        var state = TradingGraphState.Initial("AAPL") with
        {
            DataOutput = new MarketAnalysis(
                Symbol: "AAPL",
                LastPrice: 100m,
                VolumeChange: 1.0m,
                Trend: "Neutral",
                ConfidenceScore: 0.3,
                Summary: "No clear signal."),
        };

        var result = await node.ExecuteAsync(state, CancellationToken.None);

        Assert.IsNotNull(result.StrategyOutput);
        Assert.AreEqual(TradeAction.Hold, result.StrategyOutput!.Action);
        Assert.IsNull(result.StrategyDebate, "Debate disabled — transcript slot must remain null.");
        Assert.AreEqual(0, responses.Count, "Coordinator should consume exactly one chat call when debate is disabled.");
    }

    [TestMethod]
    public async Task Debate_OnRefinement_InjectsPreviousRiskRejectionIntoSeed()
    {
        var responses = new Queue<string>(
        [
            "BULL: Reduce size and keep momentum thesis.",
            "BEAR: Confidence is still too weak for a full-size position.",
            "{\"symbol\":\"AAPL\",\"action\":\"Buy\",\"quantity\":0.5,\"reasoning\":\"Reduced size addresses the rejected risk concern.\",\"confidence\":0.72,\"trailPercent\":5}",
        ]);
        var capturedPrompts = new List<string>();
        var node = BuildNodeWithChatResponses(
            responses,
            debateEnabled: true,
            turnsPerSide: 1,
            capturedPrompts: capturedPrompts);

        var rejectedDecision = new TradingDecision(
            "AAPL",
            TradeAction.Buy,
            2m,
            "Initial momentum buy.",
            0.78,
            CurrentPrice: 175m,
            EstimatedCost: 350m,
            TrailPercent: 5m);
        var state = TradingGraphState.Initial("AAPL") with
        {
            DataOutput = new MarketAnalysis(
                Symbol: "AAPL",
                LastPrice: 175m,
                VolumeChange: 1.4m,
                Trend: "Bullish",
                ConfidenceScore: 0.78,
                Summary: "Uptrend intact, but sizing may be aggressive."),
            RefinementCount = 1,
            RiskOutput = new RiskAssessment(
                RiskVerdict.Rejected,
                rejectedDecision,
                "Position size exceeds the allowed cap; reduce the quantity and explain why the tighter sizing is still justified."),
        };

        var result = await node.ExecuteAsync(state, CancellationToken.None);

        Assert.IsNotNull(result.StrategyDebate, "Refinement loops should still run a fresh debate.");
        Assert.AreEqual(2, result.StrategyDebate!.Count, "One turn per side should yield a fresh two-turn transcript on refinement.");
        Assert.AreEqual(3, capturedPrompts.Count);
        StringAssert.Contains(capturedPrompts[0], "## Risk rejection to address (attempt #1):");
        StringAssert.Contains(capturedPrompts[0], "Position size exceeds the allowed cap; reduce the quantity and explain why the tighter sizing is still justified.");
        StringAssert.Contains(capturedPrompts[0], "You must directly address these rejected-risk concerns in your argument.");
        Assert.IsFalse(capturedPrompts[0].Contains("Now output ONLY the final JSON decision object.", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Debate_LogsPerTurnActivityWithDebateTurnMetadata()
    {
        var responses = new Queue<string>(
        [
            "BULL: case A",
            "BEAR: case B",
            "{\"symbol\":\"AAPL\",\"action\":\"Hold\",\"quantity\":0,\"reasoning\":\"Coordinator decides Hold.\",\"confidence\":0.5}",
        ]);
        var activityLogs = new List<ActivityLog>();
        var activityMock = new Mock<IActivityLogger>();
        activityMock
            .Setup(l => l.LogActivityAsync(It.IsAny<ActivityLog>(), It.IsAny<CancellationToken>()))
            .Callback<ActivityLog, CancellationToken>((log, _) => activityLogs.Add(log))
            .Returns(Task.CompletedTask);

        var node = BuildNodeWithChatResponses(
            responses, debateEnabled: true, turnsPerSide: 1,
            activityLoggerOverride: activityMock.Object);

        var state = TradingGraphState.Initial("AAPL") with
        {
            DataOutput = new MarketAnalysis(
                Symbol: "AAPL",
                LastPrice: 175m,
                VolumeChange: 1.5m,
                Trend: "Bullish",
                ConfidenceScore: 0.7,
                Summary: "Mixed but leaning bull."),
        };

        await node.ExecuteAsync(state, CancellationToken.None);

        var debateTurnLogs = activityLogs
            .Where(l => l.Metadata is not null
                && l.Metadata.TryGetValue("type", out var t)
                && t as string == "debate-turn")
            .ToList();
        Assert.AreEqual(2, debateTurnLogs.Count, "Each debate turn should produce a debate-turn activity log.");
        CollectionAssert.AreEqual(
            new[] { "Bull", "Bear" },
            debateTurnLogs.Select(l => l.Metadata!["role"] as string).ToArray());
        Assert.IsTrue(debateTurnLogs.All(l => l.Metadata!.ContainsKey("fullMessage")));
        Assert.IsTrue(debateTurnLogs.All(l => l.Metadata!.ContainsKey("turnIndex")));
        Assert.IsTrue(debateTurnLogs.All(l => l.Metadata!["symbol"] as string == "AAPL"));
    }

    [TestMethod]
    public async Task Debate_CancellationTokenCancelsBeforeCoordinator()
    {
        var responses = new Queue<string>(
        [
            "BULL: never consumed",
            "BEAR: never consumed",
            "{\"symbol\":\"AAPL\",\"action\":\"Hold\",\"quantity\":0,\"reasoning\":\".\",\"confidence\":0.0}",
        ]);
        var node = BuildNodeWithChatResponses(responses, debateEnabled: true, turnsPerSide: 1);

        var state = TradingGraphState.Initial("AAPL") with
        {
            DataOutput = new MarketAnalysis(
                Symbol: "AAPL",
                LastPrice: 175m,
                VolumeChange: 1.0m,
                Trend: "Bullish",
                ConfidenceScore: 0.7,
                Summary: "Test."),
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => node.ExecuteAsync(state, cts.Token));
    }

    // ── Test helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="StrategyNode"/> backed by a single Moq'd
    /// <see cref="IChatClient"/> that returns <paramref name="responses"/> in
    /// FIFO order across all agents (bull, bear, coordinator). The mocked
    /// <see cref="IAlpacaService"/> throws on account fetch so the prompt is
    /// built without account context — sufficient for the debate flow.
    /// </summary>
    private static StrategyNode BuildNodeWithChatResponses(
        Queue<string> responses,
        bool debateEnabled,
        int turnsPerSide,
        IActivityLogger? activityLoggerOverride = null,
        List<string>? capturedPrompts = null)
    {
        var chatMock = new Mock<IChatClient>();
        chatMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                capturedPrompts?.Add(string.Join("\n", messages.Select(m => m.Text)));
                if (responses.Count == 0)
                    throw new InvalidOperationException("No more canned chat responses available.");

                var text = responses.Dequeue();
                return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));
            });

        var alpacaMock = new Mock<IAlpacaService>();
        alpacaMock
            .Setup(a => a.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("test: no account context"));

        var activity = activityLoggerOverride ?? Mock.Of<IActivityLogger>();
        var config = Options.Create(new StrategyHoundConfig
        {
            DebateEnabled = debateEnabled,
            DebateTurnsPerSide = turnsPerSide,
        });

        return new StrategyNode(chatMock.Object, alpacaMock.Object, activity, config, loggerFactory: null);
    }
}
