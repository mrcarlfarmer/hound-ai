using Hound.Trading.Graph;

namespace Hound.Trading.Tests.Graph;

[TestClass]
public class LlmResponseParserTests
{
    [TestMethod]
    public void ExtractJson_PlainJson_ReturnsUnchanged()
    {
        var input = """{"symbol":"AAPL","lastPrice":150.0}""";
        var result = LlmResponseParser.ExtractJson(input);
        Assert.AreEqual(input, result);
    }

    [TestMethod]
    public void ExtractJson_MarkdownCodeFence_StripsWrapper()
    {
        var input = """
            ```json
            {"symbol":"AAPL","lastPrice":150.0}
            ```
            """;
        var result = LlmResponseParser.ExtractJson(input);
        Assert.AreEqual("""{"symbol":"AAPL","lastPrice":150.0}""", result);
    }

    [TestMethod]
    public void ExtractJson_MarkdownFenceNoLanguage_StripsWrapper()
    {
        var input = """
            ```
            {"symbol":"AAPL"}
            ```
            """;
        var result = LlmResponseParser.ExtractJson(input);
        Assert.AreEqual("""{"symbol":"AAPL"}""", result);
    }

    [TestMethod]
    public void ExtractJson_JsonEmbeddedInText_ExtractsObject()
    {
        var input = """Here is the result: {"symbol":"AAPL","lastPrice":150.0} hope that helps!""";
        var result = LlmResponseParser.ExtractJson(input);
        Assert.AreEqual("""{"symbol":"AAPL","lastPrice":150.0}""", result);
    }

    [TestMethod]
    public void ExtractJson_EmptyBraces_ReturnsBraces()
    {
        var result = LlmResponseParser.ExtractJson("{}");
        Assert.AreEqual("{}", result);
    }

    [TestMethod]
    public void ExtractJson_NoJson_ReturnsOriginalTrimmed()
    {
        var result = LlmResponseParser.ExtractJson("  no json here  ");
        Assert.AreEqual("no json here", result);
    }

    [TestMethod]
    public void ExtractJson_ThinkBlock_StripsThinkingAndExtractsJson()
    {
        var input = """
            <think>
            Let me think about this... the price is {high} so I should reject.
            Wait, maybe I should approve. Let me recalculate.
            </think>
            {"verdict":"Approved","reasoning":"Trade is within limits"}
            """;
        var result = LlmResponseParser.ExtractJson(input);
        Assert.AreEqual("""{"verdict":"Approved","reasoning":"Trade is within limits"}""", result);
    }

    [TestMethod]
    public void ExtractJson_ThinkBlockWithJsonFragments_ExtractsLastBalancedObject()
    {
        var input = """
            <think>
            The proposed trade {"symbol":"AAPL"} looks risky because 10 shares at $285 = $2850.
            </think>
            {"verdict":"Rejected","decision":{"symbol":"AAPL","action":"Buy","quantity":10},"reasoning":"Exceeds 20% limit"}
            """;
        var result = LlmResponseParser.ExtractJson(input);
        Assert.IsTrue(result.StartsWith("""{"verdict":"Rejected"""));
        Assert.IsTrue(result.EndsWith("""Exceeds 20% limit"}"""));
    }

    [TestMethod]
    public void ExtractJson_ChainOfThoughtWithoutThinkTags_ExtractsLastObject()
    {
        var input = """
            Let me check: 20% of 1000 is 200. At 285, max shares = floor(200/285)=0.
            This seems odd. {"verdict":"Modified","adjustedQuantity":0}
            """;
        var result = LlmResponseParser.ExtractJson(input);
        Assert.AreEqual("""{"verdict":"Modified","adjustedQuantity":0}""", result);
    }
}
