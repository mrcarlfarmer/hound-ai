using Hound.Grocery.Config;
using Hound.Grocery.Nodes;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Services;

/// <summary>The outcome of a learning pass: the merged document and a list of human-readable changes.</summary>
public record LearnResult(PreferencesDocument Document, IReadOnlyList<string> Changes)
{
    /// <summary><c>true</c> when the pass produced at least one change.</summary>
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>
/// The deterministic preference-learning engine (spec §10). Given the existing
/// <see cref="PreferencesDocument"/> plus a batch of purchase observations and new
/// dislikes, it produces a merged document and a change list. Pure and side-effect
/// free — persistence is <see cref="PreferenceService.UpdateAsync"/>'s job.
/// <para>
/// <b>Read-merge-write safety:</b> items not seen in the batch are preserved
/// verbatim, favourite/staple flags are only ever promoted (never cleared), and a
/// confident/human-curated mapping is not overwritten unless its confidence is
/// below <see cref="LearnerSettings.RelearnBelowConfidence"/>.
/// </para>
/// </summary>
public class PreferenceLearner
{
    private readonly LearnerSettings _settings;

    public PreferenceLearner(IOptions<LearnerSettings> options)
    {
        _settings = options.Value;
    }

    public LearnResult Learn(
        PreferencesDocument existing,
        IReadOnlyList<PurchaseObservation> observations,
        IReadOnlyList<string>? newDislikes = null)
    {
        var changes = new List<string>();

        // Preserve existing mappings in order, keyed case-insensitively by item.
        var order = new List<string>();
        var map = new Dictionary<string, ProductPreference>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in existing.Mappings)
        {
            var key = m.Item.Trim();
            if (!map.ContainsKey(key))
            {
                map[key] = m;
                order.Add(key);
            }
        }

        var groups = observations
            .Where(o => !string.IsNullOrWhiteSpace(o.Item))
            .GroupBy(o => o.Item.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var item = group.Key;
            var obs = group.ToList();
            map.TryGetValue(item, out var prior);

            var learned = LearnItem(item, obs, prior);
            if (!map.ContainsKey(item))
            {
                order.Add(item);
            }

            map[item] = learned.Pref;
            changes.AddRange(learned.Changes);
        }

        // Merge dislikes (dedupe, case-insensitive, order-preserving).
        var dislikes = new List<string>(existing.Dislikes);
        foreach (var raw in newDislikes ?? [])
        {
            var dislike = raw.Trim();
            if (dislike.Length == 0 || dislikes.Any(d => d.Equals(dislike, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            dislikes.Add(dislike);
            changes.Add($"added dislike: {dislike}");
        }

        // A newly disliked product clears any mapping that currently prefers it.
        foreach (var key in order.ToList())
        {
            var pref = map[key];
            if (pref.PreferredProduct is { } product
                && dislikes.Any(d => ContainsCi(product, d) || ContainsCi(d, product)))
            {
                map[key] = pref with { PreferredProduct = null, Confidence = 0.0 };
                changes.Add($"cleared disliked product for {key}");
            }
        }

        var mappings = order.Select(k => map[k]).ToList();
        return new LearnResult(new PreferencesDocument(mappings, dislikes), changes);
    }

    private (ProductPreference Pref, List<string> Changes) LearnItem(
        string item, List<PurchaseObservation> obs, ProductPreference? prior)
    {
        var changes = new List<string>();

        var topProduct = MostFrequentProduct(obs, prior?.PreferredProduct);
        var observationCount = obs.Count;
        var distinctDates = obs.Select(o => o.Date).Distinct().Count();

        // Decide the resulting preferred product.
        string? resultProduct;
        bool reinforced;
        if (prior?.PreferredProduct is { } existingProduct)
        {
            if (EqualsCi(existingProduct, topProduct))
            {
                resultProduct = existingProduct;
                reinforced = true;
            }
            else if (prior.Confidence < _settings.RelearnBelowConfidence)
            {
                resultProduct = topProduct;
                reinforced = false;
                if (topProduct is not null)
                {
                    changes.Add($"remapped {item} → \"{topProduct}\" (was low-confidence)");
                }
            }
            else
            {
                // Trusted mapping diverges from new evidence — keep it untouched.
                resultProduct = existingProduct;
                reinforced = false;
            }
        }
        else
        {
            resultProduct = topProduct;
            reinforced = false;
            if (prior is null && topProduct is not null)
            {
                changes.Add($"learned {item} → \"{topProduct}\"");
            }
        }

        // Confidence: accrue when reinforcing, reset to evidence when (re)learning,
        // hold steady when a trusted mapping was kept against divergent evidence.
        double confidence;
        if (reinforced)
        {
            confidence = Clamp((prior?.Confidence ?? 0.0) + _settings.ConfidenceStep * observationCount);
            if (prior is not null && confidence > prior.Confidence)
            {
                changes.Add($"raised confidence for {item} to {Fmt(confidence)}");
            }
        }
        else if (prior is not null && EqualsCi(prior.PreferredProduct, resultProduct))
        {
            confidence = prior.Confidence; // kept-divergent: unchanged
        }
        else
        {
            confidence = Clamp(_settings.ConfidenceStep * observationCount);
        }

        // Quantity: rolling average of the observed quantities, blended with prior.
        var observedMean = obs.Average(o => o.Quantity);
        var newQty = prior?.UsualQuantity is { } priorQty
            ? Math.Round((priorQty + observedMean) / 2.0, 2, MidpointRounding.AwayFromZero)
            : Math.Round(observedMean, 2, MidpointRounding.AwayFromZero);
        if (prior?.UsualQuantity is not { } pq || Math.Abs(pq - newQty) > 0.0001)
        {
            if (prior is not null)
            {
                changes.Add($"usual qty for {item} now {Fmt(newQty)}");
            }
        }

        // Favourite/staple: promote only (never downgrade existing flags).
        var isFavourite = (prior?.IsFavourite ?? false) || observationCount >= _settings.FavouriteThreshold;
        if (isFavourite && prior?.IsFavourite != true)
        {
            changes.Add($"promoted {item} to favourite");
        }

        var isStaple = (prior?.IsStaple ?? false) || distinctDates >= _settings.StapleThreshold;
        if (isStaple && prior?.IsStaple != true)
        {
            changes.Add($"promoted {item} to staple");
        }

        return (new ProductPreference(item, resultProduct, newQty, confidence, isFavourite, isStaple), changes);
    }

    /// <summary>
    /// The most-frequently chosen product in a batch. Ties prefer the existing
    /// preferred product when it's among the leaders, else the first one observed
    /// (so the result is deterministic regardless of grouping order).
    /// </summary>
    private static string? MostFrequentProduct(List<PurchaseObservation> obs, string? existingPreferred)
    {
        var named = obs.Where(o => !string.IsNullOrWhiteSpace(o.ChosenProduct)).ToList();
        if (named.Count == 0)
        {
            return null;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var firstIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < named.Count; i++)
        {
            var product = named[i].ChosenProduct.Trim();
            counts[product] = counts.GetValueOrDefault(product) + 1;
            if (!firstIndex.ContainsKey(product))
            {
                firstIndex[product] = i;
            }
        }

        var maxCount = counts.Values.Max();
        var leaders = counts.Where(kv => kv.Value == maxCount).Select(kv => kv.Key).ToList();

        if (existingPreferred is not null && leaders.Any(l => EqualsCi(l, existingPreferred)))
        {
            return leaders.First(l => EqualsCi(l, existingPreferred));
        }

        return leaders.OrderBy(l => firstIndex[l]).First();
    }

    private double Clamp(double value) =>
        Math.Round(Math.Clamp(value, 0.0, _settings.MaxConfidence), 2, MidpointRounding.AwayFromZero);

    private static bool EqualsCi(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsCi(string haystack, string needle) =>
        needle.Length > 0 && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string Fmt(double value) => PreferencesSerializer.FormatNumber(value);
}
