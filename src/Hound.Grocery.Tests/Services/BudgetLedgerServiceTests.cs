using Hound.Grocery.Config;
using Hound.Grocery.Nodes;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class BudgetLedgerServiceTests
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Init()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-tests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static BudgetSettings Settings(
        int cycleStartDay = 25,
        decimal weeklyTarget = 140.00m,
        int flexPercent = 10,
        decimal monthlyCap = 600.00m) => new()
        {
            CycleStartDay = cycleStartDay,
            WeeklyTarget = weeklyTarget,
            WeeklyFlexPercent = flexPercent,
            MonthlyCap = monthlyCap,
        };

    private static BudgetLedgerService CreateService(
        int cycleStartDay = 25, decimal weeklyTarget = 140.00m, int flexPercent = 10, decimal monthlyCap = 600.00m) =>
        new(Options.Create(Settings(cycleStartDay, weeklyTarget, flexPercent, monthlyCap)));

    private BudgetLedgerService CreatePersistentService(
        int cycleStartDay = 25, decimal weeklyTarget = 140.00m, int flexPercent = 10, decimal monthlyCap = 600.00m)
    {
        var stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = _tempDir }));
        return new BudgetLedgerService(
            Options.Create(Settings(cycleStartDay, weeklyTarget, flexPercent, monthlyCap)),
            stateFiles);
    }

    // ── Window maths ───────────────────────────────────────────────────────────

    [TestMethod]
    public void CurrentCycleStart_OnOrAfterAnchor_ReturnsThisMonthAnchor()
    {
        var service = CreateService(cycleStartDay: 25);

        var result = service.CurrentCycleStart(new DateOnly(2026, 6, 28));

        Assert.AreEqual(new DateOnly(2026, 6, 25), result);
    }

    [TestMethod]
    public void CurrentCycleStart_BeforeAnchor_ReturnsPreviousMonthAnchor()
    {
        var service = CreateService(cycleStartDay: 25);

        var result = service.CurrentCycleStart(new DateOnly(2026, 6, 10));

        Assert.AreEqual(new DateOnly(2026, 5, 25), result);
    }

    [TestMethod]
    public void WeeklyFlexAmount_IsTenPercentOfTarget()
    {
        var service = CreateService(weeklyTarget: 140.00m, flexPercent: 10);

        Assert.AreEqual(14.00m, service.WeeklyFlexAmount());
    }

    [TestMethod]
    public void WeeklyUpperThreshold_IsTargetPlusFlex()
    {
        var service = CreateService(weeklyTarget: 140.00m, flexPercent: 10);

        Assert.AreEqual(154.00m, service.WeeklyUpperThreshold());
    }

    [TestMethod]
    public void NextCycleStart_IsOneMonthAfterCurrent()
    {
        var service = CreateService(cycleStartDay: 25);

        Assert.AreEqual(new DateOnly(2026, 7, 25), service.NextCycleStart(new DateOnly(2026, 6, 28)));
    }

    [TestMethod]
    public void CurrentWeekStart_IsAnchoredToCycleStartInSevenDayBuckets()
    {
        var service = CreateService(cycleStartDay: 25);

        // Cycle starts 2026-06-25. Day 0–6 → 06-25; day 7–13 → 07-02.
        Assert.AreEqual(new DateOnly(2026, 6, 25), service.CurrentWeekStart(new DateOnly(2026, 6, 25)));
        Assert.AreEqual(new DateOnly(2026, 6, 25), service.CurrentWeekStart(new DateOnly(2026, 7, 1)));
        Assert.AreEqual(new DateOnly(2026, 7, 2), service.CurrentWeekStart(new DateOnly(2026, 7, 2)));
        Assert.AreEqual(new DateOnly(2026, 7, 2), service.CurrentWeekStart(new DateOnly(2026, 7, 8)));
    }

    [TestMethod]
    public void CurrentWeekStart_AtCycleRollover_ResetsToNewCycleAnchor()
    {
        var service = CreateService(cycleStartDay: 25);

        // 2026-07-24 is still in the June cycle (last partial week).
        Assert.AreEqual(new DateOnly(2026, 6, 25), service.CurrentCycleStart(new DateOnly(2026, 7, 24)));
        // 2026-07-25 rolls into the new cycle — week resets to the new anchor.
        Assert.AreEqual(new DateOnly(2026, 7, 25), service.CurrentCycleStart(new DateOnly(2026, 7, 25)));
        Assert.AreEqual(new DateOnly(2026, 7, 25), service.CurrentWeekStart(new DateOnly(2026, 7, 25)));
    }

    // ── SpendToDate ────────────────────────────────────────────────────────────

    [TestMethod]
    public void SpendToDate_SumsOnlyCurrentWeekAndCurrentCycleEntries()
    {
        var service = CreateService(cycleStartDay: 25);
        var asOf = new DateOnly(2026, 7, 3); // cycle 06-25, week 07-02

        var entries = new List<BudgetLedgerEntry>
        {
            // Same week + same cycle.
            new(new DateOnly(2026, 7, 2), new DateOnly(2026, 6, 25), new DateOnly(2026, 7, 2), 40m, "Ok"),
            // Same cycle, earlier week.
            new(new DateOnly(2026, 6, 26), new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 25), 100m, "Ok"),
            // Previous cycle — excluded entirely.
            new(new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 29), 200m, "Ok"),
        };

        var (weekSpend, cycleSpend) = service.SpendToDate(entries, asOf);

        Assert.AreEqual(40m, weekSpend);
        Assert.AreEqual(140m, cycleSpend);
    }

    // ── EvaluateBasket ─────────────────────────────────────────────────────────

    [TestMethod]
    public void EvaluateBasket_WithinFlexAndUnderCap_IsOk()
    {
        var service = CreateService();

        var verdict = service.EvaluateBasket(basketSubtotal: 150m, priorWeekSpend: 0m, priorCycleSpend: 0m);

        Assert.AreEqual(BudgetOutcome.Ok, verdict.Outcome);
        Assert.IsFalse(verdict.RequiresApproval);
        Assert.IsTrue(verdict.WithinBudget);
        Assert.AreEqual(0m, verdict.WeeklyOverage);
        Assert.AreEqual(0m, verdict.CycleOverage);
    }

    [TestMethod]
    public void EvaluateBasket_OverWeeklyFlexOnly_IsWeeklyOver()
    {
        var service = CreateService();

        // Threshold 154; basket 170 → 16 over weekly, cycle 170 < 600.
        var verdict = service.EvaluateBasket(basketSubtotal: 170m, priorWeekSpend: 0m, priorCycleSpend: 0m);

        Assert.AreEqual(BudgetOutcome.WeeklyOver, verdict.Outcome);
        Assert.IsTrue(verdict.RequiresApproval);
        Assert.AreEqual(16m, verdict.WeeklyOverage);
        Assert.AreEqual(0m, verdict.CycleOverage);
    }

    [TestMethod]
    public void EvaluateBasket_OverCycleCapOnly_IsCycleOver()
    {
        var service = CreateService();

        // Prior cycle spend 590, basket 15 → cycle 605 > cap but weekly fine.
        var verdict = service.EvaluateBasket(basketSubtotal: 15m, priorWeekSpend: 0m, priorCycleSpend: 590m);

        Assert.AreEqual(BudgetOutcome.CycleOver, verdict.Outcome);
        Assert.IsTrue(verdict.RequiresApproval);
        Assert.AreEqual(0m, verdict.WeeklyOverage);
        Assert.AreEqual(5m, verdict.CycleOverage);
        Assert.AreEqual(605m, verdict.ProjectedCycleTotal);
    }

    [TestMethod]
    public void EvaluateBasket_OverBothWeeklyAndCycle_IsBoth()
    {
        var service = CreateService();

        // Basket 200 → weekly 200 (>154) and cycle 590+200=790 (>600).
        var verdict = service.EvaluateBasket(basketSubtotal: 200m, priorWeekSpend: 0m, priorCycleSpend: 590m);

        Assert.AreEqual(BudgetOutcome.Both, verdict.Outcome);
        Assert.IsTrue(verdict.RequiresApproval);
        Assert.AreEqual(46m, verdict.WeeklyOverage);
        Assert.AreEqual(190m, verdict.CycleOverage);
    }

    [TestMethod]
    public void EvaluateBasket_PercentOver_IsRelativeToThresholdAndCap()
    {
        var service = CreateService();

        var verdict = service.EvaluateBasket(basketSubtotal: 200m, priorWeekSpend: 0m, priorCycleSpend: 590m);

        // Weekly: 46 / 154 * 100 ≈ 29.87; cycle: 190 / 600 * 100 ≈ 31.67.
        Assert.AreEqual(29.87, Math.Round(verdict.WeeklyPercentOver, 2));
        Assert.AreEqual(31.67, Math.Round(verdict.CyclePercentOver, 2));
    }

    [TestMethod]
    public void EvaluateBasket_PriorWeekSpendPushesOverThreshold()
    {
        var service = CreateService();

        // Prior week 140 + basket 30 = 170 > 154 → weekly over by 16.
        var verdict = service.EvaluateBasket(basketSubtotal: 30m, priorWeekSpend: 140m, priorCycleSpend: 140m);

        Assert.AreEqual(BudgetOutcome.WeeklyOver, verdict.Outcome);
        Assert.AreEqual(16m, verdict.WeeklyOverage);
        Assert.AreEqual(170m, verdict.ProjectedWeeklyTotal);
    }

    // ── Ledger persistence round-trip ──────────────────────────────────────────

    [TestMethod]
    public void RenderLedger_ThenParse_RoundTripsEntries()
    {
        var service = CreateService();
        var entries = new List<BudgetLedgerEntry>
        {
            new(new DateOnly(2026, 6, 26), new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 25), 142.50m, "Ok"),
            new(new DateOnly(2026, 7, 3), new DateOnly(2026, 6, 25), new DateOnly(2026, 7, 2), 170.00m, "WeeklyOver"),
        };

        var markdown = service.RenderLedger(entries);
        var parsed = BudgetLedgerService.ParseEntries(markdown);

        Assert.AreEqual(2, parsed.Count);
        CollectionAssert.AreEqual(entries, parsed.ToList());
    }

    [TestMethod]
    public void ParseEntries_EmptyOrMissing_ReturnsEmpty()
    {
        Assert.AreEqual(0, BudgetLedgerService.ParseEntries(string.Empty).Count);
        Assert.AreEqual(0, BudgetLedgerService.ParseEntries("# Budget ledger\n\nNo builds yet.").Count);
    }

    [TestMethod]
    public async Task RecordBuildAsync_ThenLoadAsync_PersistsAndReloads()
    {
        var service = CreatePersistentService();
        var entry = new BudgetLedgerEntry(
            new DateOnly(2026, 6, 26), new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 25), 142.50m, "Ok");

        await service.RecordBuildAsync(entry);
        var reloaded = await service.LoadAsync();

        Assert.AreEqual(1, reloaded.Count);
        Assert.AreEqual(entry, reloaded[0]);
    }

    [TestMethod]
    public async Task RecordBuildAsync_AppendsAcrossCalls()
    {
        var service = CreatePersistentService();

        await service.RecordBuildAsync(new BudgetLedgerEntry(
            new DateOnly(2026, 6, 26), new DateOnly(2026, 6, 25), new DateOnly(2026, 6, 25), 100m, "Ok"));
        await service.RecordBuildAsync(new BudgetLedgerEntry(
            new DateOnly(2026, 7, 3), new DateOnly(2026, 6, 25), new DateOnly(2026, 7, 2), 170m, "WeeklyOver"));

        var reloaded = await service.LoadAsync();

        Assert.AreEqual(2, reloaded.Count);
        Assert.AreEqual(270m, reloaded.Sum(e => e.BasketSubtotal));
    }

    [TestMethod]
    public async Task LoadAsync_WhenNoStateStore_ReturnsEmpty()
    {
        var service = CreateService();

        var entries = await service.LoadAsync();

        Assert.AreEqual(0, entries.Count);
    }
}
