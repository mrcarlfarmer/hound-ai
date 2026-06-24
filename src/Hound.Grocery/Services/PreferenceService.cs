using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Services;

/// <summary>
/// A single learned mapping from a loose list item to a preferred Sainsbury's
/// product (spec §8, §10). Populated over time by LearnerHound (Phase 6); during
/// cold start the file is usually empty and these are absent.
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
/// favourite/staple flags and dislikes, preserving the human-readable sketch
/// <c>milk → "Sainsbury's British Semi Skimmed Milk 2.27L" · usual qty 2 · confidence 0.8</c>.
/// <para>
/// <b>Cold start:</b> LearnerHound (Phase 6) has not run yet, so the file is
/// usually empty. Every method degrades gracefully — a missing or empty file
/// yields <see cref="PreferencesDocument.Empty"/> and <see cref="ResolveAsync"/>
/// returns <c>null</c>.
/// </para>
/// </summary>
public class PreferenceService
{
    private const string Heading = "# Preferences";
    private const string MappingsHeading = "## Product mappings";
    private const string DislikesHeading = "## Dislikes";

    private static readonly Regex MappingLineRegex = new(
        @"^-\s*(?<item>.+?)\s*(?:→|->)\s*(?:""(?<product>.*?)""|\((?<none>none|any)\))(?<tags>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex QtyRegex = new(
        @"usual\s+qty\s+(?<qty>\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConfidenceRegex = new(
        @"confidence\s+(?<c>\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
                await _stateFiles.WriteAsync(GroceryStateFile.Preferences, Template(), cancellationToken);
                _logger?.LogInformation("preferences.md was empty/missing; wrote a template scaffold.");
                return PreferencesDocument.Empty;
            }

            return ParseDocument(raw);
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

    // ── Internals ─────────────────────────────────────────────────────────────

    internal static PreferencesDocument ParseDocument(string markdown)
    {
        var mappings = new List<ProductPreference>();
        var dislikes = new List<string>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return PreferencesDocument.Empty;
        }

        var section = Section.None;
        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("##", StringComparison.Ordinal))
            {
                section = line.StartsWith(MappingsHeading, StringComparison.OrdinalIgnoreCase) ? Section.Mappings
                    : line.StartsWith(DislikesHeading, StringComparison.OrdinalIgnoreCase) ? Section.Dislikes
                    : Section.None;
                continue;
            }

            if (line.StartsWith('#') || !line.StartsWith('-'))
            {
                continue;
            }

            switch (section)
            {
                case Section.Mappings:
                    var pref = ParseMapping(line);
                    if (pref is not null)
                    {
                        mappings.Add(pref);
                    }

                    break;

                case Section.Dislikes:
                    var dislike = line.TrimStart('-').Trim();
                    if (dislike.Length > 0)
                    {
                        dislikes.Add(dislike);
                    }

                    break;
            }
        }

        return new PreferencesDocument(mappings, dislikes);
    }

    internal static ProductPreference? ParseMapping(string line)
    {
        var match = MappingLineRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var item = match.Groups["item"].Value.Trim();
        if (item.Length == 0)
        {
            return null;
        }

        var product = match.Groups["none"].Success
            ? null
            : NullIfBlank(match.Groups["product"].Value.Trim());

        var tags = match.Groups["tags"].Value;

        double? qty = QtyRegex.Match(tags) is { Success: true } qm
            ? double.Parse(qm.Groups["qty"].Value, CultureInfo.InvariantCulture)
            : null;

        var confidence = ConfidenceRegex.Match(tags) is { Success: true } cm
            ? double.Parse(cm.Groups["c"].Value, CultureInfo.InvariantCulture)
            : 0.0;

        var isFavourite = tags.Contains("favourite", StringComparison.OrdinalIgnoreCase);
        var isStaple = tags.Contains("staple", StringComparison.OrdinalIgnoreCase);

        return new ProductPreference(item, product, qty, confidence, isFavourite, isStaple);
    }

    internal static string RenderDocument(PreferencesDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Heading);
        builder.AppendLine();
        builder.AppendLine("Learned product mappings, typical quantities, favourites and dislikes.");
        builder.AppendLine("Both users can edit this file directly; LearnerHound refines it over time.");
        builder.AppendLine();
        builder.AppendLine(MappingsHeading);
        builder.AppendLine();
        foreach (var m in document.Mappings)
        {
            builder.AppendLine(RenderMapping(m));
        }

        builder.AppendLine();
        builder.AppendLine(DislikesHeading);
        builder.AppendLine();
        foreach (var d in document.Dislikes)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {d}");
        }

        return builder.ToString();
    }

    internal static string RenderMapping(ProductPreference pref)
    {
        var product = pref.PreferredProduct is null ? "(none)" : $"\"{pref.PreferredProduct}\"";
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"- {pref.Item} → {product}");
        if (pref.UsualQuantity is { } qty)
        {
            builder.Append(CultureInfo.InvariantCulture, $" · usual qty {FormatNumber(qty)}");
        }

        builder.Append(CultureInfo.InvariantCulture, $" · confidence {FormatNumber(pref.Confidence)}");
        if (pref.IsFavourite)
        {
            builder.Append(" · favourite");
        }

        if (pref.IsStaple)
        {
            builder.Append(" · staple");
        }

        return builder.ToString();
    }

    private static string Template()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Heading);
        builder.AppendLine();
        builder.AppendLine("Learned product mappings, typical quantities, favourites and dislikes.");
        builder.AppendLine("Both users can edit this file directly; LearnerHound refines it over time.");
        builder.AppendLine();
        builder.AppendLine(MappingsHeading);
        builder.AppendLine();
        builder.AppendLine("<!-- e.g. - milk → \"Sainsbury's British Semi Skimmed Milk 2.27L\" · usual qty 2 · confidence 0.8 · favourite -->");
        builder.AppendLine();
        builder.AppendLine(DislikesHeading);
        builder.AppendLine();
        builder.AppendLine("<!-- e.g. - value-range ready meals -->");
        return builder.ToString();
    }

    private static string FormatNumber(double value) =>
        value == Math.Floor(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string? NullIfBlank(string value) => value.Length == 0 ? null : value;

    private static bool NameEquals(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private enum Section
    {
        None,
        Mappings,
        Dislikes,
    }
}
