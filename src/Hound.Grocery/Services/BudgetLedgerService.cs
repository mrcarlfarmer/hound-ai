using Hound.Grocery.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Services;

/// <summary>
/// Maintains the pay-cycle budget ledger and computes weekly/monthly windows
/// (spec §9). Used by <c>BudgetHound</c>.
/// <para>
/// <b>Phase 1 scaffold:</b> exposes pay-cycle window helpers only; projected
/// totals, reconciliation against the scraped subtotal and ledger persistence
/// arrive in Phase 5.
/// </para>
/// </summary>
public class BudgetLedgerService
{
    private readonly BudgetSettings _settings;
    private readonly ILogger<BudgetLedgerService>? _logger;

    public BudgetLedgerService(IOptions<BudgetSettings> options, ILoggerFactory? loggerFactory = null)
    {
        _settings = options.Value;
        _logger = loggerFactory?.CreateLogger<BudgetLedgerService>();
    }

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

    /// <summary>The weekly flex band as an absolute amount (± of the weekly target).</summary>
    public decimal WeeklyFlexAmount() =>
        _settings.WeeklyTarget * _settings.WeeklyFlexPercent / 100m;

    /// <summary>The upper weekly threshold (target + flex) that triggers flag-and-ask.</summary>
    public decimal WeeklyUpperThreshold() => _settings.WeeklyTarget + WeeklyFlexAmount();
}
