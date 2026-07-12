namespace Hound.Grocery.Services;

/// <summary>
/// A cold-start planning suggestion for a single loose item: the search term to
/// run on the grocery site, an optional preferred-product hint and a sensible
/// default weekly quantity.
/// </summary>
public record PlanSuggestion(string SearchTerm, string? ProductHint, double Quantity);

/// <summary>
/// Proposes a product hint + default quantity for a loose item that has no
/// learned preference yet (cold start, before LearnerHound has run). The
/// LLM-backed implementation (<see cref="LlmPlannerAssistant"/>) calls the keyed
/// <c>default</c> chat client; tests substitute a fake so no live model is hit.
/// </summary>
public interface IPlannerAssistant
{
    Task<PlanSuggestion> SuggestAsync(string item, CancellationToken cancellationToken);
}
