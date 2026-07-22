using System.Globalization;
using System.Text;
using Hound.Grocery.Nodes;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Services;

/// <summary>
/// Reads and appends <c>purchase-history.md</c> — the append-only log of past
/// builds that feeds LearnerHound (spec §8, §10). ShopperHound will append real
/// rows once it exists; until then the file is driven synthetically in tests.
/// <para>
/// Format (a single <c>## Purchases</c> markdown table, round-trip stable):
/// <code>
/// | date | item | product | qty |
/// |------|------|---------|-----|
/// | 2026-06-21 | milk | Sainsbury's Semi Skimmed 2.27L | 2 |
/// </code>
/// </para>
/// </summary>
public class PurchaseHistoryService
{
    private const string Heading = "# Purchase history";
    private const string PurchasesHeading = "## Purchases";

    private readonly StateFileService _stateFiles;
    private readonly ILogger<PurchaseHistoryService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PurchaseHistoryService(StateFileService stateFiles, ILoggerFactory? loggerFactory = null)
    {
        _stateFiles = stateFiles;
        _logger = loggerFactory?.CreateLogger<PurchaseHistoryService>();
    }

    /// <summary>Reads and parses the purchase log; empty when absent.</summary>
    public async Task<IReadOnlyList<PurchaseObservation>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var raw = await _stateFiles.ReadAsync(GroceryStateFile.PurchaseHistory, cancellationToken);
            return Parse(raw);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Appends observations to the log, creating it if needed.</summary>
    public async Task AppendAsync(IReadOnlyList<PurchaseObservation> observations, CancellationToken cancellationToken = default)
    {
        if (observations.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var raw = await _stateFiles.ReadAsync(GroceryStateFile.PurchaseHistory, cancellationToken);
            var all = Parse(raw).ToList();
            all.AddRange(observations);
            await _stateFiles.WriteAsync(GroceryStateFile.PurchaseHistory, Render(all), cancellationToken);
            _logger?.LogDebug("Appended {Count} purchase observation(s) to purchase-history.md.", observations.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static IReadOnlyList<PurchaseObservation> Parse(string markdown)
    {
        var observations = new List<PurchaseObservation>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return observations;
        }

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length != 4)
            {
                continue;
            }

            // Skip the header and separator rows: a valid data row starts with a date.
            if (!DateOnly.TryParseExact(cells[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var item = cells[1];
            var product = cells[2];
            if (item.Length == 0 || !double.TryParse(cells[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var qty))
            {
                continue;
            }

            observations.Add(new PurchaseObservation(item, product, qty, date));
        }

        return observations;
    }

    internal static string Render(IReadOnlyList<PurchaseObservation> observations)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Heading);
        builder.AppendLine();
        builder.AppendLine("Append-only log of past builds; feeds LearnerHound.");
        builder.AppendLine();
        builder.AppendLine(PurchasesHeading);
        builder.AppendLine();
        builder.AppendLine("| date | item | product | qty |");
        builder.AppendLine("|------|------|---------|-----|");
        foreach (var o in observations)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {o.Date:yyyy-MM-dd} | {o.Item} | {o.ChosenProduct} | {FormatNumber(o.Quantity)} |");
        }

        return builder.ToString();
    }

    private static string FormatNumber(double value) =>
        value == Math.Floor(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
}
