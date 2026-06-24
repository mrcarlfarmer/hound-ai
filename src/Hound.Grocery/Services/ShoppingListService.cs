using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hound.Grocery.Nodes;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Services;

/// <summary>
/// Domain wrapper over <c>shopping-list.md</c> (spec §8). Parses the loose list
/// from markdown, applies add / remove / set-quantity mutations and renders it
/// back, preserving the human-readable sketch
/// <c>- [ ] milk  (qty: ~2)  ·  added by Carl 2026-06-21</c>.
/// <para>
/// Items are keyed case-insensitively by name. All read-modify-write cycles are
/// serialised with a semaphore so concurrent Telegram messages can't corrupt the
/// file.
/// </para>
/// </summary>
public class ShoppingListService
{
    private const string Heading = "# Shopping list";

    private static readonly Regex ItemLineRegex = new(
        @"^-\s*\[[ xX]?\]\s*(?<name>.+?)\s*(?:\(qty:\s*~?\s*(?<qty>[\d]+(?:\.[\d]+)?)\s*\))?\s*(?:·\s*added by\s+(?<by>.+?)\s+(?<date>\d{4}-\d{2}-\d{2}))?\s*$",
        RegexOptions.Compiled);

    private readonly StateFileService _stateFiles;
    private readonly ILogger<ShoppingListService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ShoppingListService(StateFileService stateFiles, ILoggerFactory? loggerFactory = null)
    {
        _stateFiles = stateFiles;
        _logger = loggerFactory?.CreateLogger<ShoppingListService>();
    }

    /// <summary>Returns the current list items in file order.</summary>
    public async Task<IReadOnlyList<ShoppingListItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadItemsAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adds an item, or updates the quantity and attribution of an existing one
    /// (matched case-insensitively by name). Returns the resulting item.
    /// </summary>
    public async Task<ShoppingListItem> AddOrUpdateAsync(
        string name,
        double quantity,
        string addedBy,
        DateTime addedAt,
        CancellationToken cancellationToken = default)
    {
        var cleanName = NormaliseName(name);
        var qty = quantity <= 0 ? 1 : quantity;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadItemsAsync(cancellationToken)).ToList();
            var index = items.FindIndex(i => NameEquals(i.Name, cleanName));
            var item = new ShoppingListItem(cleanName, qty, addedBy, addedAt);

            if (index >= 0)
            {
                items[index] = item;
            }
            else
            {
                items.Add(item);
            }

            await WriteItemsAsync(items, cancellationToken);
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes an item by name. Returns <c>true</c> if it was present.</summary>
    public async Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        var cleanName = NormaliseName(name);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadItemsAsync(cancellationToken)).ToList();
            var removed = items.RemoveAll(i => NameEquals(i.Name, cleanName)) > 0;
            if (removed)
            {
                await WriteItemsAsync(items, cancellationToken);
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Sets the quantity of an existing item. If the item is absent it is added
    /// (attributed to <paramref name="addedBy"/>). Returns the resulting item.
    /// </summary>
    public async Task<ShoppingListItem> SetQuantityAsync(
        string name,
        double quantity,
        string addedBy,
        DateTime addedAt,
        CancellationToken cancellationToken = default)
    {
        var cleanName = NormaliseName(name);
        var qty = quantity <= 0 ? 1 : quantity;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var items = (await ReadItemsAsync(cancellationToken)).ToList();
            var index = items.FindIndex(i => NameEquals(i.Name, cleanName));
            ShoppingListItem item;

            if (index >= 0)
            {
                var existing = items[index];
                item = existing with { Quantity = qty };
                items[index] = item;
            }
            else
            {
                item = new ShoppingListItem(cleanName, qty, addedBy, addedAt);
                items.Add(item);
            }

            await WriteItemsAsync(items, cancellationToken);
            return item;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Renders the current list as a short human-readable summary.</summary>
    public async Task<string> RenderSummaryAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetItemsAsync(cancellationToken);
        if (items.Count == 0)
        {
            return "The shopping list is empty.";
        }

        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Shopping list ({items.Count} item{(items.Count == 1 ? "" : "s")}):");
        foreach (var item in items)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"• {item.Name} (qty ~{FormatQuantity(item.Quantity)})");
        }

        return builder.ToString().TrimEnd();
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task<List<ShoppingListItem>> ReadItemsAsync(CancellationToken cancellationToken)
    {
        var raw = await _stateFiles.ReadAsync(GroceryStateFile.ShoppingList, cancellationToken);
        return ParseItems(raw);
    }

    private async Task WriteItemsAsync(IReadOnlyList<ShoppingListItem> items, CancellationToken cancellationToken)
    {
        await _stateFiles.WriteAsync(GroceryStateFile.ShoppingList, RenderMarkdown(items), cancellationToken);
        _logger?.LogDebug("Shopping list now has {Count} item(s).", items.Count);
    }

    internal static List<ShoppingListItem> ParseItems(string markdown)
    {
        var items = new List<ShoppingListItem>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return items;
        }

        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var match = ItemLineRegex.Match(trimmed);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var qty = match.Groups["qty"].Success
                ? double.Parse(match.Groups["qty"].Value, CultureInfo.InvariantCulture)
                : 1;

            var addedBy = match.Groups["by"].Success ? match.Groups["by"].Value.Trim() : "unknown";

            var addedAt = match.Groups["date"].Success
                ? DateTime.ParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DateTime.UtcNow.Date;

            items.Add(new ShoppingListItem(name, qty, addedBy, addedAt));
        }

        return items;
    }

    internal static string RenderMarkdown(IReadOnlyList<ShoppingListItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Heading);
        builder.AppendLine();

        foreach (var item in items)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"- [ ] {item.Name}  (qty: ~{FormatQuantity(item.Quantity)})  ·  added by {item.AddedBy} {item.AddedAt:yyyy-MM-dd}");
        }

        return builder.ToString();
    }

    internal static string FormatQuantity(double quantity) =>
        quantity == Math.Floor(quantity)
            ? ((long)quantity).ToString(CultureInfo.InvariantCulture)
            : quantity.ToString("0.##", CultureInfo.InvariantCulture);

    private static string NormaliseName(string name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();

    private static bool NameEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
