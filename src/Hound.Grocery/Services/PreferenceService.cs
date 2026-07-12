using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Services;

/// <summary>
/// A single learned mapping from a loose list item to a preferred Sainsbury's
/// product (spec §8, §10). Populated over time by LearnerHound; during cold start
/// the file is usually empty and these are absent.
/// </summary>
public record ProductPreference(
    string Item,
    string? PreferredProduct,
    double? UsualQuantity,
    double Confidence,
    bool IsFavourite,
    bool IsStaple);

/// <summary>The parsed contents of <c>preferences.md</c>: mappings + dislikes.</summary>
public record PreferencesDocument(
    IReadOnlyList<ProductPreference> Mappings,
    IReadOnlyList<string> Dislikes)
{
    public static PreferencesDocument Empty { get; } = new([], []);
}

/// <summary>
/// Domain wrapper over <c>preferences.md</c> (spec §8, §10). Reads (and creates a
/// template if missing) the learned product mappings, typical quantities,
/// favourite/staple flags and dislikes. All parsing/serialisation is delegated to
/// the shared <see cref="PreferencesSerializer"/> so the reader here and the
/// writer (LearnerHound, via <see cref="UpdateAsync"/>) can never drift.
/// <para>
/// <b>Cold start:</b> LearnerHound may not have run yet, so the file is often
/// empty. Every method degrades gracefully — a missing or empty file yields
/// <see cref="PreferencesDocument.Empty"/> and <see cref="ResolveAsync"/> returns
/// <c>null</c>.
/// </para>
/// </summary>
public class PreferenceService
{
    private readonly StateFileService _stateFiles;
    private readonly ILogger<PreferenceService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PreferenceService(StateFileService stateFiles, ILoggerFactory? loggerFactory = null)
    {
        _stateFiles = stateFiles;
        _logger = loggerFactory?.CreateLogger<PreferenceService>();
    }

    /// <summary>
    /// Loads the preferences document. If the file is missing or empty a template
    /// scaffold is written so both users have something to edit, and an empty
    /// document is returned.
    /// </summary>
    public async Task<PreferencesDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var raw = await _stateFiles.ReadAsync(GroceryStateFile.Preferences, cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
            {
                await _stateFiles.WriteAsync(GroceryStateFile.Preferences, PreferencesSerializer.Template(), cancellationToken);
                _logger?.LogInformation("preferences.md was empty/missing; wrote a template scaffold.");
                return PreferencesDocument.Empty;
            }

            return PreferencesSerializer.Parse(raw);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Resolves a loose item name to its learned preference (case-insensitive
    /// exact match), or <c>null</c> when there is no mapping.
    /// </summary>
    public async Task<ProductPreference?> ResolveAsync(string item, CancellationToken cancellationToken = default)
    {
        var doc = await LoadAsync(cancellationToken);
        return doc.Mappings.FirstOrDefault(m => NameEquals(m.Item, item));
    }

    /// <summary>Serialises and writes <paramref name="document"/> to <c>preferences.md</c>.</summary>
    public async Task SaveAsync(PreferencesDocument document, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _stateFiles.WriteAsync(GroceryStateFile.Preferences, PreferencesSerializer.Render(document), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Atomically read-merge-writes the preferences. Loads the current document
    /// (parsing whatever is on disk — never the template scaffold, so nothing is
    /// lost), passes it to <paramref name="mutate"/>, then writes the result back
    /// under the same lock. This is how LearnerHound persists learning without
    /// clobbering human-curated or previously-learned entries.
    /// </summary>
    public async Task<PreferencesDocument> UpdateAsync(
        Func<PreferencesDocument, PreferencesDocument> mutate, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var raw = await _stateFiles.ReadAsync(GroceryStateFile.Preferences, cancellationToken);
            var current = string.IsNullOrWhiteSpace(raw) ? PreferencesDocument.Empty : PreferencesSerializer.Parse(raw);
            var updated = mutate(current);
            await _stateFiles.WriteAsync(GroceryStateFile.Preferences, PreferencesSerializer.Render(updated), cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Back-compat helpers (delegate to the shared serializer) ─────────────────

    internal static PreferencesDocument ParseDocument(string markdown) => PreferencesSerializer.Parse(markdown);

    internal static ProductPreference? ParseMapping(string line) => PreferencesSerializer.ParseMapping(line);

    internal static string RenderDocument(PreferencesDocument document) => PreferencesSerializer.Render(document);

    internal static string RenderMapping(ProductPreference pref) => PreferencesSerializer.RenderMapping(pref);

    private static bool NameEquals(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
