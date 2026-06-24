using Hound.Grocery.Services;
using Microsoft.Extensions.AI;
using Moq;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class LlmPlannerAssistantTests
{
    private static Mock<IChatClient> ChatReturning(string text)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        return mock;
    }

    [TestMethod]
    public void ParseSuggestion_ValidJson_MapsAllFields()
    {
        const string json = """{"search_term":"semi skimmed milk","product_hint":"Sainsbury's Semi Skimmed 2.27L","quantity":2}""";

        var suggestion = LlmPlannerAssistant.ParseSuggestion(json, "milk");

        Assert.IsNotNull(suggestion);
        Assert.AreEqual("semi skimmed milk", suggestion!.SearchTerm);
        Assert.AreEqual("Sainsbury's Semi Skimmed 2.27L", suggestion.ProductHint);
        Assert.AreEqual(2d, suggestion.Quantity);
    }

    [TestMethod]
    public void ParseSuggestion_NullHint_AndMissingQuantity_AreHandled()
    {
        const string json = """{"search_term":"bin bags","product_hint":null}""";

        var suggestion = LlmPlannerAssistant.ParseSuggestion(json, "bin bags");

        Assert.IsNotNull(suggestion);
        Assert.IsNull(suggestion!.ProductHint);
        Assert.AreEqual(1d, suggestion.Quantity, "missing/invalid quantity falls back to 1");
    }

    [TestMethod]
    public void ParseSuggestion_BlankSearchTerm_FallsBackToItem()
    {
        const string json = """{"search_term":"","product_hint":"x","quantity":1}""";

        var suggestion = LlmPlannerAssistant.ParseSuggestion(json, "eggs");

        Assert.AreEqual("eggs", suggestion!.SearchTerm);
    }

    [TestMethod]
    public void ParseSuggestion_Malformed_ReturnsNull()
    {
        Assert.IsNull(LlmPlannerAssistant.ParseSuggestion("not json", "milk"));
    }

    [TestMethod]
    public async Task SuggestAsync_BlankItem_ReturnsFallback_WithoutCallingModel()
    {
        var mock = ChatReturning("{}");

        var suggestion = await new LlmPlannerAssistant(mock.Object).SuggestAsync("   ", default);

        Assert.AreEqual(string.Empty, suggestion.SearchTerm);
        Assert.AreEqual(1d, suggestion.Quantity);
        mock.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task SuggestAsync_ValidModelReply_ProducesSuggestion()
    {
        var mock = ChatReturning("""{"search_term":"oat milk","product_hint":"Alpro Oat","quantity":3}""");

        var suggestion = await new LlmPlannerAssistant(mock.Object).SuggestAsync("oat milk", default);

        Assert.AreEqual("oat milk", suggestion.SearchTerm);
        Assert.AreEqual("Alpro Oat", suggestion.ProductHint);
        Assert.AreEqual(3d, suggestion.Quantity);
    }

    [TestMethod]
    public async Task SuggestAsync_ModelThrows_DegradesToFallback()
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model offline"));

        var suggestion = await new LlmPlannerAssistant(mock.Object).SuggestAsync("milk", default);

        Assert.AreEqual("milk", suggestion.SearchTerm);
        Assert.IsNull(suggestion.ProductHint);
        Assert.AreEqual(1d, suggestion.Quantity);
    }
}
