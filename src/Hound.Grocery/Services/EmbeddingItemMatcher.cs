using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Services;

/// <summary>
/// <see cref="IItemMatcher"/> backed by the registered <c>embeddinggemma</c>
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>. Embeds the item and the
/// candidate keys, then returns the candidate with the highest cosine similarity
/// above <see cref="MinimumScore"/>.
/// <para>
/// Any embedding failure degrades to <c>null</c> (no fuzzy match) so the planner
/// can fall back to its other strategies and never crashes.
/// </para>
/// </summary>
public class EmbeddingItemMatcher : IItemMatcher
{
    /// <summary>Cosine similarity below which a match is treated as too weak.</summary>
    internal const double MinimumScore = 0.75;

    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly ILogger<EmbeddingItemMatcher>? _logger;

    public EmbeddingItemMatcher(
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        ILoggerFactory? loggerFactory = null)
    {
        _embeddings = embeddings;
        _logger = loggerFactory?.CreateLogger<EmbeddingItemMatcher>();
    }

    public async Task<ItemMatch?> FindBestMatchAsync(
        string item,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item) || candidates.Count == 0)
        {
            return null;
        }

        // An exact (case-insensitive) match short-circuits the model entirely.
        var exact = candidates.FirstOrDefault(c => string.Equals(c, item, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new ItemMatch(exact, 1.0);
        }

        try
        {
            var inputs = new List<string> { item };
            inputs.AddRange(candidates);

            var embeddings = await _embeddings.GenerateAsync(inputs, cancellationToken: cancellationToken);
            if (embeddings.Count != inputs.Count)
            {
                return null;
            }

            var itemVector = embeddings[0].Vector;
            string? best = null;
            var bestScore = double.NegativeInfinity;

            for (var i = 0; i < candidates.Count; i++)
            {
                var score = CosineSimilarity(itemVector.Span, embeddings[i + 1].Vector.Span);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidates[i];
                }
            }

            return best is not null && bestScore >= MinimumScore
                ? new ItemMatch(best, bestScore)
                : null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Embedding match failed for '{Item}'; skipping fuzzy match.", item);
            return null;
        }
    }

    internal static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 0.0;
        }

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA == 0 || magB == 0)
        {
            return 0.0;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
