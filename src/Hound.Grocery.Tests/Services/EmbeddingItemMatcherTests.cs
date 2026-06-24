using Hound.Grocery.Services;
using Microsoft.Extensions.AI;
using Moq;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class EmbeddingItemMatcherTests
{
    private static Mock<IEmbeddingGenerator<string, Embedding<float>>> GeneratorReturning(
        Dictionary<string, float[]> vectors)
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> values, EmbeddingGenerationOptions? _, CancellationToken __) =>
                new GeneratedEmbeddings<Embedding<float>>(
                    values.Select(v => new Embedding<float>(vectors[v]))));
        return mock;
    }

    [TestMethod]
    public async Task FindBestMatch_ExactCandidate_ShortCircuits_WithoutEmbedding()
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        var matcher = new EmbeddingItemMatcher(mock.Object);

        var result = await matcher.FindBestMatchAsync("Milk", ["milk", "bread"], default);

        Assert.IsNotNull(result);
        Assert.AreEqual("milk", result!.Candidate);
        Assert.AreEqual(1.0, result.Score);
        mock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task FindBestMatch_PicksClosestAboveThreshold()
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["semi skimmed milk"] = [1f, 0f],
            ["milk"] = [1f, 0f],
            ["bread"] = [0f, 1f],
        };
        var matcher = new EmbeddingItemMatcher(GeneratorReturning(vectors).Object);

        var result = await matcher.FindBestMatchAsync("semi skimmed milk", ["milk", "bread"], default);

        Assert.IsNotNull(result);
        Assert.AreEqual("milk", result!.Candidate);
        Assert.IsTrue(result.Score >= EmbeddingItemMatcher.MinimumScore);
    }

    [TestMethod]
    public async Task FindBestMatch_AllBelowThreshold_ReturnsNull()
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["mystery"] = [1f, 1f],
            ["milk"] = [1f, 0f],
            ["bread"] = [0f, 1f],
        };
        var matcher = new EmbeddingItemMatcher(GeneratorReturning(vectors).Object);

        var result = await matcher.FindBestMatchAsync("mystery", ["milk", "bread"], default);

        Assert.IsNull(result, "cosine ~0.707 is below the 0.75 threshold");
    }

    [TestMethod]
    public async Task FindBestMatch_NoCandidates_ReturnsNull()
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>(MockBehavior.Strict);
        var matcher = new EmbeddingItemMatcher(mock.Object);

        var result = await matcher.FindBestMatchAsync("milk", [], default);

        Assert.IsNull(result);
        mock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task FindBestMatch_GeneratorThrows_DegradesToNull()
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embeddings offline"));
        var matcher = new EmbeddingItemMatcher(mock.Object);

        var result = await matcher.FindBestMatchAsync("oat milk", ["milk", "bread"], default);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void CosineSimilarity_IdenticalVectors_IsOne()
    {
        var score = EmbeddingItemMatcher.CosineSimilarity(new float[] { 1, 2, 3 }, new float[] { 1, 2, 3 });

        Assert.AreEqual(1.0, score, 1e-9);
    }

    [TestMethod]
    public void CosineSimilarity_MismatchedLengths_IsZero()
    {
        var score = EmbeddingItemMatcher.CosineSimilarity(new float[] { 1, 2 }, new float[] { 1, 2, 3 });

        Assert.AreEqual(0.0, score);
    }
}
