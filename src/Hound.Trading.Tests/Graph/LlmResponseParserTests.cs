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
}
