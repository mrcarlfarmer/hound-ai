namespace Hound.Grocery.Services;

/// <summary>
/// Fuzzy matcher that maps a loose list item (e.g. "semi skimmed milk") to the
/// closest existing key among a set of candidates (e.g. learned preference keys),
/// using embedding similarity (spec §10). Behind an interface so the planner is
/// testable without the live <c>embeddinggemma</c> model.
/// </summary>
public interface IItemMatcher
{
    /// <summary>
    /// Returns the candidate most similar to <paramref name="item"/>, or
    /// <c>null</c> when there are no candidates or none clears the confidence
    /// threshold.
    /// </summary>
    Task<ItemMatch?> FindBestMatchAsync(
        string item,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken);
}

/// <summary>A fuzzy match result: the winning candidate and its cosine score.</summary>
public record ItemMatch(string Candidate, double Score);
