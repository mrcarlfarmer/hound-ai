using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Config;
using Hound.Grocery.Graph;
using Hound.Grocery.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Nodes;

/// <summary>
/// Turns the loose <c>shopping-list.md</c> into a concrete <see cref="ShoppingPlan"/>
/// (spec §6, §10): per item a search term, a preferred-product hint, a target
/// quantity, a priority and favourite/staple flags.
/// <para>
/// Resolution order per item: (1) an exact learned preference, (2) a fuzzy
/// embedding match against existing preference keys, (3) cold-start fallback that
/// asks the <c>default</c> LLM for a product hint + default weekly quantity. With
/// no preferences file yet (the common case before LearnerHound runs) every item
/// takes the cold-start path; nothing here crashes on an empty/missing file.
/// </para>
/// </summary>
public class PlannerHound : INode
{
    public string NodeId => "planner-hound";
    public string PackId => GroceryPack.PackId;

    private readonly IActivityLogger _activityLogger;
    private readonly ShoppingListService _shoppingList;
    private readonly PreferenceService _preferences;
    private readonly IItemMatcher _matcher;
    private readonly IPlannerAssistant _assistant;
    private readonly BudgetSettings _budget;
    private readonly ILogger<PlannerHound>? _logger;

    public PlannerHound(
        IActivityLogger activityLogger,
        ShoppingListService shoppingList,
        PreferenceService preferences,
        IItemMatcher matcher,
        IPlannerAssistant assistant,
        IOptions<BudgetSettings> budgetOptions,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _shoppingList = shoppingList;
        _preferences = preferences;
        _matcher = matcher;
        _assistant = assistant;
        _budget = budgetOptions.Value;
        _logger = loggerFactory?.CreateLogger<PlannerHound>();
    }

    public async Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("PlannerHound building a plan for run {RunId}", state.RunId);

        var plan = await BuildPlanAsync(cancellationToken);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(PlannerHound),
            Message = $"PlanBuilt: {plan.Items.Count} item(s) planned",
            Severity = ActivitySeverity.Success,
            Metadata = new Dictionary<string, object>
            {
                ["event"] = "PlanBuilt",
                ["itemCount"] = plan.Items.Count,
                ["weeklyBudgetTarget"] = plan.WeeklyBudgetTarget ?? 0m,
            },
        }, cancellationToken);

        return state with { CurrentNode = NodeId, Phase = GroceryPhase.Planning, Plan = plan };
    }

    /// <summary>
    /// Reads the loose list + preferences and resolves every item into a
    /// <see cref="PlannedItem"/>. Exposed for unit testing without the graph.
    /// </summary>
    internal async Task<ShoppingPlan> BuildPlanAsync(CancellationToken cancellationToken)
    {
        var listItems = await _shoppingList.GetItemsAsync(cancellationToken);
        var preferences = await _preferences.LoadAsync(cancellationToken);
        var keys = preferences.Mappings.Select(m => m.Item).ToList();

        var planned = new List<PlannedItem>(listItems.Count);
        foreach (var item in listItems)
        {
            planned.Add(await PlanItemAsync(item, preferences, keys, cancellationToken));
        }

        return new ShoppingPlan(planned, _budget.WeeklyTarget, DateTime.UtcNow);
    }

    private async Task<PlannedItem> PlanItemAsync(
        ShoppingListItem item,
        PreferencesDocument preferences,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var pref = ResolveExact(preferences, item.Name)
            ?? await ResolveFuzzyAsync(preferences, keys, item.Name, cancellationToken);

        if (pref is not null)
        {
            var quantity = ResolveQuantity(item.Quantity, pref.UsualQuantity, suggested: null);
            var searchTerm = string.IsNullOrWhiteSpace(pref.PreferredProduct) ? item.Name : pref.PreferredProduct!;
            return new PlannedItem(
                RawListText: item.Name,
                SearchTerm: searchTerm,
                PreferredProduct: pref.PreferredProduct,
                TargetQuantity: quantity,
                Priority: pref.IsFavourite || pref.IsStaple ? PlanPriority.High : PlanPriority.Normal,
                IsFavourite: pref.IsFavourite,
                IsStaple: pref.IsStaple);
        }

        // Cold start: no learned preference, ask the LLM for a sensible hint + qty.
        var suggestion = await _assistant.SuggestAsync(item.Name, cancellationToken);
        var coldQuantity = ResolveQuantity(item.Quantity, learned: null, suggested: suggestion.Quantity);
        var coldSearchTerm = string.IsNullOrWhiteSpace(suggestion.SearchTerm) ? item.Name : suggestion.SearchTerm;
        return new PlannedItem(
            RawListText: item.Name,
            SearchTerm: coldSearchTerm,
            PreferredProduct: suggestion.ProductHint,
            TargetQuantity: coldQuantity,
            Priority: PlanPriority.Normal,
            IsFavourite: false,
            IsStaple: false);
    }

    private static ProductPreference? ResolveExact(PreferencesDocument preferences, string name) =>
        preferences.Mappings.FirstOrDefault(
            m => string.Equals(m.Item, name, StringComparison.OrdinalIgnoreCase));

    private async Task<ProductPreference?> ResolveFuzzyAsync(
        PreferencesDocument preferences,
        IReadOnlyList<string> keys,
        string name,
        CancellationToken cancellationToken)
    {
        if (keys.Count == 0)
        {
            return null;
        }

        var match = await _matcher.FindBestMatchAsync(name, keys, cancellationToken);
        if (match is null)
        {
            return null;
        }

        _logger?.LogDebug("Fuzzy-matched '{Item}' to preference '{Key}' (score {Score:0.##}).",
            name, match.Candidate, match.Score);

        return preferences.Mappings.FirstOrDefault(
            m => string.Equals(m.Item, match.Candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Target-quantity precedence: an explicit list quantity (the user bumped it
    /// above the default of 1) wins; otherwise the learned typical quantity, then
    /// the cold-start suggestion, then a safe default.
    /// </summary>
    internal static double ResolveQuantity(double listQuantity, double? learned, double? suggested)
    {
        if (listQuantity > 1)
        {
            return listQuantity;
        }

        if (learned is > 0)
        {
            return learned.Value;
        }

        if (suggested is > 0)
        {
            return suggested.Value;
        }

        return listQuantity > 0 ? listQuantity : 1;
    }
}
