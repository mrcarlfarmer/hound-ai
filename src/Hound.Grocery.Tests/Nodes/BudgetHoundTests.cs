using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Config;
using Hound.Grocery.Graph;
using Hound.Grocery.Nodes;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace Hound.Grocery.Tests.Nodes;

[TestClass]
public class BudgetHoundTests
{
    private Mock<IActivityLogger> _logger = null!;
    private string _tempDir = null!;
    private StateFileService _stateFiles = null!;
    private BudgetLedgerService _ledger = null!;
    private BudgetHound _hound = null!;

    [TestInitialize]
    public void Setup()
    {
        _logger = new Mock<IActivityLogger>();
        _tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-tests", Guid.NewGuid().ToString("N"));
        _stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = _tempDir }));
        _ledger = new BudgetLedgerService(
            Options.Create(new BudgetSettings
            {
                CycleStartDay = 25,
                WeeklyTarget = 140m,
                WeeklyFlexPercent = 10,
                MonthlyCap = 600m,
            }),
            _stateFiles);
        _hound = new BudgetHound(_logger.Object, _ledger);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static BasketResult Basket(decimal subtotal) =>
        new([new BasketLine("p1", "Item", 1d, subtotal, false, false, null)], subtotal);

    [TestMethod]
    public void NodeId_And_PackId_FollowConventions()
    {
        Assert.AreEqual("budget-hound", _hound.NodeId);
        Assert.AreEqual(GroceryPack.PackId, _hound.PackId);
    }

    [TestMethod]
    public async Task ReconcileAsync_WithinFlex_ReturnsOkAndLogsSuccess()
    {
        var verdict = await _hound.ReconcileAsync(Basket(150m), new DateOnly(2026, 6, 26), CancellationToken.None);

        Assert.AreEqual(BudgetOutcome.Ok, verdict.Outcome);
        Assert.IsFalse(verdict.RequiresApproval);
        _logger.Verify(l => l.LogActivityAsync(
            It.Is<ActivityLog>(a =>
                a.HoundName == nameof(BudgetHound)
                && a.Severity == ActivitySeverity.Success
                && a.Metadata != null
                && (string)a.Metadata["event"] == "BudgetChecked"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ReconcileAsync_OverWeeklyFlex_FlagsAndLogsWarning()
    {
        var verdict = await _hound.ReconcileAsync(Basket(170m), new DateOnly(2026, 6, 26), CancellationToken.None);

        Assert.AreEqual(BudgetOutcome.WeeklyOver, verdict.Outcome);
        Assert.IsTrue(verdict.RequiresApproval);
        _logger.Verify(l => l.LogActivityAsync(
            It.Is<ActivityLog>(a => a.Severity == ActivitySeverity.Warning),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ReconcileAsync_EmptyBasket_IsOk()
    {
        var verdict = await _hound.ReconcileAsync(new BasketResult([], 0m), new DateOnly(2026, 6, 26), CancellationToken.None);

        Assert.AreEqual(BudgetOutcome.Ok, verdict.Outcome);
        Assert.AreEqual(0m, verdict.BasketSubtotal);
    }

    [TestMethod]
    public async Task ReconcileAsync_RecordsLedgerEntry()
    {
        await _hound.ReconcileAsync(Basket(150m), new DateOnly(2026, 6, 26), CancellationToken.None);

        var entries = await _ledger.LoadAsync();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(150m, entries[0].BasketSubtotal);
        Assert.AreEqual(new DateOnly(2026, 6, 25), entries[0].CycleStart);
        Assert.AreEqual("Ok", entries[0].Outcome);
    }

    [TestMethod]
    public async Task ReconcileAsync_AccumulatesPriorSpendWithinWeek()
    {
        var asOf = new DateOnly(2026, 6, 26);

        // First basket 100 (Ok, week 100). Second basket 100 → week 200 > 154 → WeeklyOver.
        await _hound.ReconcileAsync(Basket(100m), asOf, CancellationToken.None);
        var second = await _hound.ReconcileAsync(Basket(100m), asOf, CancellationToken.None);

        Assert.AreEqual(BudgetOutcome.WeeklyOver, second.Outcome);
        Assert.AreEqual(200m, second.ProjectedWeeklyTotal);

        var entries = await _ledger.LoadAsync();
        Assert.AreEqual(2, entries.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_PopulatesVerdictSlotAndPhase()
    {
        var state = GroceryGraphState.Initial() with { Basket = Basket(150m) };

        var next = await _hound.ExecuteAsync(state, CancellationToken.None);

        Assert.IsNotNull(next.BudgetVerdict);
        Assert.AreEqual(GroceryPhase.Budgeting, next.Phase);
        Assert.AreEqual("budget-hound", next.CurrentNode);
    }

    [TestMethod]
    public async Task ExecuteAsync_NullBasket_TreatedAsEmptyAndOk()
    {
        var state = GroceryGraphState.Initial();

        var next = await _hound.ExecuteAsync(state, CancellationToken.None);

        Assert.IsNotNull(next.BudgetVerdict);
        Assert.AreEqual(BudgetOutcome.Ok, next.BudgetVerdict!.Outcome);
    }
}
