using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Hound.Grocery.Services;

/// <summary>
/// The <b>single source of truth</b> for the <c>preferences.md</c> on-disk format
/// (spec §8, §10). Both the reader (<see cref="PreferenceService"/>) and the
/// writer (<c>LearnerHound</c>, via <see cref="PreferenceService.UpdateAsync"/>)
/// parse and serialise through this one type so the two can never drift.
/// <para>
/// Format (stable, round-trip lossless):
/// <code>
/// # Preferences
/// ## Product mappings
/// - milk → "Sainsbury's British Semi Skimmed Milk 2.27L" · usual qty 2 · confidence 0.8 · favourite
/// ## Dislikes
/// - value-range ready meals
/// </code>
/// Accepts either the Unicode arrow <c>→</c> or ASCII <c>-&gt;</c> on read; always
/// emits <c>→</c> on write.
/// </para>
/// </summary>
public static class PreferencesSerializer
{
    public const string Heading = "# Preferences";
    public const string MappingsHeading = "## Product mappings";
    public const string DislikesHeading = "## Dislikes";

    private const string Blurb =
        "Learned product mappings, typical quantities, favourites and dislikes.";
    private const string EditNote =
        "Both users can edit this file directly; LearnerHound refines it over time.";

    private static readonly Regex MappingLineRegex = new(
        @"^-\s*(?<item>.+?)\s*(?:→|->)\s*(?:""(?<product>.*?)""|\((?<none>none|any)\))(?<tags>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex QtyRegex = new(
        @"usual\s+qty\s+(?<qty>\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ConfidenceRegex = new(
        @"confidence\s+(?<c>\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Parse ───────────────────────────────────────────────────────────────────

    /// <summary>Parses raw markdown into a <see cref="PreferencesDocument"/>.</summary>
    public static PreferencesDocument Parse(string markdown)
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

    /// <summary>Parses a single <c>- item → "product" · tags</c> mapping line, or <c>null</c>.</summary>
    public static ProductPreference? ParseMapping(string line)
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

    // ── Render ──────────────────────────────────────────────────────────────────

    /// <summary>Serialises a document back to markdown in the canonical format.</summary>
    public static string Render(PreferencesDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Heading);
        builder.AppendLine();
        builder.AppendLine(Blurb);
        builder.AppendLine(EditNote);
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

    /// <summary>Serialises a single mapping line.</summary>
    public static string RenderMapping(ProductPreference pref)
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

    /// <summary>The scaffold written when <c>preferences.md</c> is missing/empty.</summary>
    public static string Template()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Heading);
        builder.AppendLine();
        builder.AppendLine(Blurb);
        builder.AppendLine(EditNote);
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

    internal static string FormatNumber(double value) =>
        value == Math.Floor(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string? NullIfBlank(string value) => value.Length == 0 ? null : value;

    private enum Section
    {
        None,
        Mappings,
        Dislikes,
    }
}
