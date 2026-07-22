using Hound.Grocery.Nodes;

namespace Hound.Grocery.Services;

/// <summary>
/// The product chosen for a planned item, plus whether it is a substitution and
/// the ranked shortlist (for the audit trace).
/// </summary>
public record RankedSelection(
    ProductCandidate? Chosen,
    bool IsSubstitution,
    string? SubstitutionReason,
    IReadOnlyList<ProductCandidate> Ranked);

/// <summary>
/// Pure, deterministic candidate ranking for <c>ShopperHound</c> (spec §10).
/// Preference order: (a) favourite, then (b) Nectar/loyalty price, then the
/// preferred-product hint / closest match, then lowest effective price. Only
/// in-stock candidates are considered. When the planned preferred product is not
/// available, the closest in-stock candidate is chosen and the result is flagged
/// as a substitution with a human-readable reason.
/// <para>
/// No browser, network or LLM calls — given the same inputs it always returns
/// the same selection, so it is exhaustively unit-tested.
/// </para>
/// </summary>
public class ProductRanker
{
    /// <summary>Ranks candidates for a planned item and picks one (or none).</summary>
    public RankedSelection Rank(PlannedItem item, IReadOnlyList<ProductCandidate> candidates)
    {
        var inStock = candidates.Where(c => c.InStock).ToList();
        var ranked = inStock.OrderByDescending(c => c.IsFavourite)
            .ThenByDescending(c => c.IsNectarPrice)
            .ThenBy(c => c.EffectivePrice)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ranked.Count == 0)
        {
            return new RankedSelection(null, IsSubstitution: false, SubstitutionReason: null, ranked);
        }

        // No specific learned product → a fresh pick from the search results; the
        // top-ranked candidate wins and it is not treated as a substitution.
        if (string.IsNullOrWhiteSpace(item.PreferredProduct))
        {
            return new RankedSelection(ranked[0], IsSubstitution: false, SubstitutionReason: null, ranked);
        }

        var preferredMatches = ranked.Where(c => MatchesPreferred(c, item.PreferredProduct!)).ToList();
        if (preferredMatches.Count > 0)
        {
            // Preferred product is available — rank within the matches.
            return new RankedSelection(preferredMatches[0], IsSubstitution: false, SubstitutionReason: null, ranked);
        }

        // Preferred product unavailable — auto-substitute the closest in-stock match.
        var chosen = ranked[0];
        var reason = $"'{item.PreferredProduct}' unavailable; substituted '{chosen.Name}'";
        return new RankedSelection(chosen, IsSubstitution: true, reason, ranked);
    }

    private static bool MatchesPreferred(ProductCandidate candidate, string preferred)
    {
        var name = candidate.Name;
        return name.Contains(preferred, StringComparison.OrdinalIgnoreCase)
            || preferred.Contains(name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.ProductId, preferred, StringComparison.OrdinalIgnoreCase);
    }
}
