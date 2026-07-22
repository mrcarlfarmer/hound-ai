using System.Globalization;
using System.Text;
using Hound.Grocery.Nodes;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Services;

/// <summary>
/// Writes <c>basket-trace.md</c> — the per-run audit trail of what ShopperHound
/// added to the live basket (spec §8, R4): every line with its quantity, price,
/// Nectar/favourite flags and any substitution reason, plus the subtotal. Runs
/// are appended so the file preserves the full history of builds.
/// </summary>
public class BasketTraceService
{
    private const string Heading = "# Basket trace";

    private readonly StateFileService _stateFiles;
    private readonly ILogger<BasketTraceService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BasketTraceService(StateFileService stateFiles, ILoggerFactory? loggerFactory = null)
    {
        _stateFiles = stateFiles;
        _logger = loggerFactory?.CreateLogger<BasketTraceService>();
    }

    /// <summary>Appends a run section for the given basket build.</summary>
    public async Task AppendRunAsync(
        string runId,
        BasketResult basket,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _stateFiles.ReadAsync(GroceryStateFile.BasketTrace, cancellationToken);
            var builder = new StringBuilder();
            if (string.IsNullOrWhiteSpace(existing))
            {
                builder.AppendLine(Heading);
                builder.AppendLine();
                builder.AppendLine("Append-only audit trail of basket builds (R4). Read-only history; never a checkout.");
                builder.AppendLine();
            }
            else
            {
                builder.Append(existing.TrimEnd('\n'));
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.AppendLine(CultureInfo.InvariantCulture, $"## Run {runId} — {at:yyyy-MM-dd HH:mm:ss} UTC");
            builder.AppendLine();
            builder.AppendLine("| product | qty | price | nectar | favourite | substitution |");
            builder.AppendLine("|---------|-----|-------|--------|-----------|--------------|");
            foreach (var line in basket.Lines)
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"| {Escape(line.ProductName)} | {FormatNumber(line.Quantity)} | {line.Price.ToString("0.00", CultureInfo.InvariantCulture)} | {YesNo(line.IsNectarPrice)} | {YesNo(line.IsFavourite)} | {Escape(line.SubstitutionReason ?? "-")} |");
            }

            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"**Subtotal:** £{basket.Subtotal.ToString("0.00", CultureInfo.InvariantCulture)} ({basket.Lines.Count} line(s))");

            await _stateFiles.WriteAsync(GroceryStateFile.BasketTrace, builder.ToString(), cancellationToken);
            _logger?.LogDebug("Appended basket trace run {RunId} ({Lines} lines).", runId, basket.Lines.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Escape(string value) => value.Replace("|", "\\|");

    private static string FormatNumber(double value) =>
        value == Math.Floor(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
}
