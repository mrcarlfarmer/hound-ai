using System.Globalization;
using System.Text;
using Hound.Grocery.Config;
using Hound.Grocery.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Services;

/// <summary>One recorded basket build in <c>budget-ledger.md</c> (spec §8, §9).</summary>
public record BudgetLedgerEntry(
    DateOnly Date,
    DateOnly CycleStart,
    DateOnly WeekStart,
    decimal BasketSubtotal,
    string Outcome);

/// <summary>
/// Maintains the pay-cycle budget ledger and computes weekly/monthly windows
/// (spec §9). Owns the deterministic budget maths (no LLM) and persistence of
/// <c>budget-ledger.md</c>. Used by <c>BudgetHound</c>.
/// </summary>
public class BudgetLedgerService
{
    private const string Heading = "# Budget ledger";
    private const string BuildsHeading = "## Builds";

    private readonly BudgetSettings _settings;
    private readonly StateFileService? _stateFiles;
    private readonly ILogger<BudgetLedgerService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BudgetLedgerService(
        IOptions<BudgetSettings> options,
        StateFileService? stateFiles = null,
        ILoggerFactory? loggerFactory = null)
    {
        _settings = options.Value;
        _stateFiles = stateFiles;
        _logger = loggerFactory?.CreateLogger<BudgetLedgerService>();
    }

    // ── Pay-cycle / week windows ───────────────────────────────────────────────

    /// <summary>
    /// Returns the start date of the pay cycle that contains <paramref name="asOf"/>,
    /// anchored on <see cref="BudgetSettings.CycleStartDay"/>.
    /// </summary>
    public DateOnly CurrentCycleStart(DateOnly asOf)
    {
        var day = Math.Clamp(_settings.CycleStartDay, 1, 28);

        var thisMonthAnchor = new DateOnly(asOf.Year, asOf.Month, day);
        if (asOf >= thisMonthAnchor)
        {
            return thisMonthAnchor;
        }

        var previous = asOf.AddMonths(-1);
        return new DateOnly(previous.Year, previous.Month, day);
    }

    /// <summary>The start of the pay cycle immediately following the one containing <paramref name="asOf"/>.</summary>
    public DateOnly NextCycleStart(DateOnly asOf) => CurrentCycleStart(asOf).AddMonths(1);

    /// <summary>
    /// The start of the budget "week" containing <paramref name="asOf"/>. Weeks are
    /// 7-day buckets anchored to the pay-cycle start, so weekly budgets stay aligned
    /// to the cycle (spec §9: "weekly budget = derived within the cycle").
    /// </summary>
    public DateOnly CurrentWeekStart(DateOnly asOf)
    {
        var cycleStart = CurrentCycleStart(asOf);
        var daysIn = asOf.DayNumber - cycleStart.DayNumber;
        var weekIndex = daysIn / 7;
        return cycleStart.AddDays(weekIndex * 7);
    }

    /// <summary>The weekly flex band as an absolute amount (± of the weekly target).</summary>
    public decimal WeeklyFlexAmount() =>
        _settings.WeeklyTarget * _settings.WeeklyFlexPercent / 100m;

    /// <summary>The upper weekly threshold (target + flex) that triggers flag-and-ask.</summary>
    public decimal WeeklyUpperThreshold() => _settings.WeeklyTarget + WeeklyFlexAmount();

    // ── Reconciliation (deterministic) ─────────────────────────────────────────

    /// <summary>
    /// Reconciles a basket subtotal against the weekly flex band and the soft
    /// pay-cycle cap, given the spend already booked this week and this cycle.
    /// Pure, deterministic and side-effect free — the verdict only ever flags and
    /// asks; it never refuses (R2/R3, spec §9).
    /// </summary>
    public BudgetVerdict EvaluateBasket(
        decimal basketSubtotal,
        decimal priorWeekSpend,
        decimal priorCycleSpend)
    {
        var projectedWeekly = priorWeekSpend + basketSubtotal;
        var projectedCycle = priorCycleSpend + basketSubtotal;

        var weeklyThreshold = WeeklyUpperThreshold();
        var weeklyOverAmount = Math.Max(0m, projectedWeekly - weeklyThreshold);
        var cycleOverAmount = Math.Max(0m, projectedCycle - _settings.MonthlyCap);

        var weeklyOver = weeklyOverAmount > 0m;
        var cycleOver = cycleOverAmount > 0m;

        var outcome = (weeklyOver, cycleOver) switch
        {
            (true, true) => BudgetOutcome.Both,
            (true, false) => BudgetOutcome.WeeklyOver,
            (false, true) => BudgetOutcome.CycleOver,
            _ => BudgetOutcome.Ok,
        };

        var weeklyPercentOver = Percent(weeklyOverAmount, weeklyThreshold);
        var cyclePercentOver = Percent(cycleOverAmount, _settings.MonthlyCap);

        return new BudgetVerdict(
            Outcome: outcome,
            BasketSubtotal: basketSubtotal,
            WeeklyTarget: _settings.WeeklyTarget,
            WeeklyThreshold: weeklyThreshold,
            ProjectedWeeklyTotal: projectedWeekly,
            WeeklyOverage: weeklyOverAmount,
            WeeklyPercentOver: weeklyPercentOver,
            MonthlyCap: _settings.MonthlyCap,
            ProjectedCycleTotal: projectedCycle,
            CycleOverage: cycleOverAmount,
            CyclePercentOver: cyclePercentOver,
            RequiresApproval: outcome != BudgetOutcome.Ok,
            Summary: BuildSummary(outcome, basketSubtotal, projectedWeekly, weeklyThreshold,
                weeklyOverAmount, projectedCycle, cycleOverAmount));
    }

    /// <summary>
    /// Sums the spend already booked in the current week and current cycle from
    /// prior ledger entries (the basket being evaluated is added separately).
    /// </summary>
    public (decimal WeekSpend, decimal CycleSpend) SpendToDate(
        IReadOnlyList<BudgetLedgerEntry> entries, DateOnly asOf)
    {
        var cycleStart = CurrentCycleStart(asOf);
        var weekStart = CurrentWeekStart(asOf);

        var weekSpend = 0m;
        var cycleSpend = 0m;
        foreach (var entry in entries)
        {
            if (entry.CycleStart == cycleStart)
            {
                cycleSpend += entry.BasketSubtotal;
            }

            if (entry.WeekStart == weekStart)
            {
                weekSpend += entry.BasketSubtotal;
            }
        }

        return (weekSpend, cycleSpend);
    }

    // ── Persistence (budget-ledger.md) ─────────────────────────────────────────

    /// <summary>Reads and parses the ledger build history; empty when absent.</summary>
    public async Task<IReadOnlyList<BudgetLedgerEntry>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_stateFiles is null)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var raw = await _stateFiles.ReadAsync(GroceryStateFile.BudgetLedger, cancellationToken);
            return ParseEntries(raw);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Appends a build entry and rewrites <c>budget-ledger.md</c>.</summary>
    public async Task RecordBuildAsync(BudgetLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        if (_stateFiles is null)
        {
            _logger?.LogDebug("No state-file store configured; skipping ledger persistence.");
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var raw = await _stateFiles.ReadAsync(GroceryStateFile.BudgetLedger, cancellationToken);
            var entries = ParseEntries(raw).ToList();
            entries.Add(entry);
            await _stateFiles.WriteAsync(GroceryStateFile.BudgetLedger, RenderLedger(entries), cancellationToken);
            _logger?.LogDebug("Recorded budget ledger entry for {Date} ({Subtotal}).", entry.Date, entry.BasketSubtotal);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static IReadOnlyList<BudgetLedgerEntry> ParseEntries(string markdown)
    {
        var entries = new List<BudgetLedgerEntry>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return entries;
        }

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length != 5)
            {
                continue;
            }

            // Skip the header row and the markdown separator row.
            if (!DateOnly.TryParseExact(cells[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            if (!DateOnly.TryParseExact(cells[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var cycleStart)
                || !DateOnly.TryParseExact(cells[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var weekStart)
                || !decimal.TryParse(cells[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var subtotal))
            {
                continue;
            }

            entries.Add(new BudgetLedgerEntry(date, cycleStart, weekStart, subtotal, cells[4]));
        }

        return entries;
    }

    internal string RenderLedger(IReadOnlyList<BudgetLedgerEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Heading);
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture,
            $"Currency: {_settings.Currency} · cycle start day: {_settings.CycleStartDay} · " +
            $"weekly target: {Money(_settings.WeeklyTarget)} · weekly flex: {_settings.WeeklyFlexPercent}% · " +
            $"monthly cap: {Money(_settings.MonthlyCap)}");
        builder.AppendLine();
        builder.AppendLine(BuildsHeading);
        builder.AppendLine();
        builder.AppendLine("| date | cycle start | week start | basket subtotal | outcome |");
        builder.AppendLine("|------|-------------|------------|-----------------|---------|");
        foreach (var e in entries)
        {
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"| {e.Date:yyyy-MM-dd} | {e.CycleStart:yyyy-MM-dd} | {e.WeekStart:yyyy-MM-dd} | {Money(e.BasketSubtotal)} | {e.Outcome} |");
        }

        return builder.ToString();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static double Percent(decimal over, decimal baseline) =>
        baseline <= 0m || over <= 0m ? 0.0 : (double)(over / baseline) * 100.0;

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string BuildSummary(
        BudgetOutcome outcome,
        decimal basketSubtotal,
        decimal projectedWeekly,
        decimal weeklyThreshold,
        decimal weeklyOver,
        decimal projectedCycle,
        decimal cycleOver)
    {
        return outcome switch
        {
            BudgetOutcome.Ok =>
                $"Basket {Money(basketSubtotal)} keeps the week at {Money(projectedWeekly)} " +
                $"(within the {Money(weeklyThreshold)} flex band). All good.",
            BudgetOutcome.WeeklyOver =>
                $"Basket {Money(basketSubtotal)} pushes the week to {Money(projectedWeekly)}, " +
                $"{Money(weeklyOver)} over the {Money(weeklyThreshold)} flex band. Approve or trim?",
            BudgetOutcome.CycleOver =>
                $"Basket {Money(basketSubtotal)} pushes the pay cycle to {Money(projectedCycle)}, " +
                $"{Money(cycleOver)} over the monthly cap. Approve or trim?",
            BudgetOutcome.Both =>
                $"Basket {Money(basketSubtotal)} is {Money(weeklyOver)} over the weekly flex band " +
                $"(week at {Money(projectedWeekly)}) and {Money(cycleOver)} over the monthly cap " +
                $"(cycle at {Money(projectedCycle)}). Approve or trim?",
            _ => string.Empty,
        };
    }
}
