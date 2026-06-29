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
public class ShopperHoundTests
{
    private Mock<IActivityLogger> _logger = null!;
    private Mock<IBrowserWorkerClient> _browser = null!;
    private string _tempDir = null!;
    private StateFileService _stateFiles = null!;
    private BasketTraceService _trace = null!;
    private PurchaseHistoryService _history = null!;
    private ShopperHound _hound = null!;

    private static readonly DateTimeOffset At = new(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);

    [TestInitialize]
    public void Setup()
    {
        _logger = new Mock<IActivityLogger>();
        _browser = new Mock<IBrowserWorkerClient>();
        _browser.Setup(b => b.LoginAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(true, "ok"));
        _browser.Setup(b => b.AddToBasketAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasketSnapshot(Array.Empty<BasketLine>(), 0m));
        _browser.Setup(b => b.GetBasketAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasketSnapshot(Array.Empty<BasketLine>(), 0m));

        _tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-tests", Guid.NewGuid().ToString("N"));
        _stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = _tempDir }));
        _trace = new BasketTraceService(_stateFiles);
        _history = new PurchaseHistoryService(_stateFiles);
        _hound = new ShopperHound(_logger.Object, _browser.Object, new ProductRanker(), _trace, _history);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static ProductCandidate Cand(
        string id, string name, decimal price, decimal? nectar = null, bool isNectar = false,
        bool favourite = false, bool inStock = true) =>
        new(id, name, price, nectar, isNectar, favourite, inStock, null, $"/groceries/product/{id}", null);

    private void SetupSearch(string term, params ProductCandidate[] candidates) =>
        _browser.Setup(b => b.SearchAsync(term, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResult(term, candidates));

    private static ShoppingPlan Plan(params PlannedItem[] items) => new(items);

    [TestMethod]
    public void NodeId_And_PackId_FollowConventions()
    {
        Assert.AreEqual("shopper-hound", _hound.NodeId);
        Assert.AreEqual(GroceryPack.PackId, _hound.PackId);
    }

    [TestMethod]
    public async Task ShopAsync_SynthesisesBasket_WhenSnapshotEmpty()
    {
        SetupSearch("milk", Cand("semi", "Semi Skimmed 2.27L", 1.45m, favourite: true));
        var plan = Plan(new PlannedItem("milk", "milk", "Semi Skimmed", 2));

        var outcome = await _hound.ShopAsync(plan, At, "run-1", CancellationToken.None);

        Assert.AreEqual(1, outcome.Basket.Lines.Count);
        var line = outcome.Basket.Lines[0];
        Assert.AreEqual("semi", line.ProductId);
        Assert.AreEqual(2d, line.Quantity);
        Assert.IsTrue(line.IsFavourite);
        Assert.AreEqual(2.90m, outcome.Basket.Subtotal);
        _browser.Verify(b => b.AddToBasketAsync("semi", 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ShopAsync_UsesSidecarSnapshot_AndEnrichesFlags()
    {
        SetupSearch("milk", Cand("semi", "Semi Skimmed 2.27L", 1.45m, favourite: true));
        _browser.Setup(b => b.GetBasketAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasketSnapshot(
                [new BasketLine("semi", "Semi Skimmed 2.27L", 2, 1.45m, false, false, null)],
                2.90m));
        var plan = Plan(new PlannedItem("milk", "milk", "Semi Skimmed", 2));

        var outcome = await _hound.ShopAsync(plan, At, "run-1", CancellationToken.None);

        Assert.AreEqual(2.90m, outcome.Basket.Subtotal);
        // The favourite flag is enriched from the chosen candidate (trolley lacks it).
        Assert.IsTrue(outcome.Basket.Lines[0].IsFavourite);
    }

    [TestMethod]
    public async Task ShopAsync_RecordsSubstitution_InBasketLine_AndTrace()
    {
        SetupSearch("oat milk", Cand("semi", "Semi Skimmed 2.27L", 1.45m));
        var plan = Plan(new PlannedItem("oat milk", "oat milk", "Oat Milk Barista", 1));

        var outcome = await _hound.ShopAsync(plan, At, "run-1", CancellationToken.None);

        var line = outcome.Basket.Lines.Single();
        Assert.IsNotNull(line.SubstitutionReason);
        StringAssert.Contains(line.SubstitutionReason, "Oat Milk Barista");

        var trace = await _stateFiles.ReadAsync(GroceryStateFile.BasketTrace);
        StringAssert.Contains(trace, "Semi Skimmed 2.27L");
        StringAssert.Contains(trace, "Oat Milk Barista");
    }

    [TestMethod]
    public async Task ShopAsync_WritesBasketTrace_WithSubtotal()
    {
        SetupSearch("milk", Cand("semi", "Semi Skimmed 2.27L", 1.45m));
        var plan = Plan(new PlannedItem("milk", "milk", null, 1));

        await _hound.ShopAsync(plan, At, "run-42", CancellationToken.None);

        var trace = await _stateFiles.ReadAsync(GroceryStateFile.BasketTrace);
        StringAssert.Contains(trace, "Run run-42");
        StringAssert.Contains(trace, "Subtotal:");
    }

    [TestMethod]
    public async Task ShopAsync_AppendsPurchaseObservations()
    {
        SetupSearch("milk", Cand("semi", "Semi Skimmed 2.27L", 1.45m));
        SetupSearch("eggs", Cand("eggs6", "Free Range Eggs x6", 1.20m));
        var plan = Plan(
            new PlannedItem("milk", "milk", null, 2),
            new PlannedItem("eggs", "eggs", null, 1));

        await _hound.ShopAsync(plan, At, "run-1", CancellationToken.None);

        var obs = await _history.LoadAsync();
        Assert.AreEqual(2, obs.Count);
        Assert.IsTrue(obs.Any(o => o.Item == "milk" && o.ChosenProduct == "Semi Skimmed 2.27L" && o.Quantity == 2));
        Assert.IsTrue(obs.Any(o => o.Item == "eggs"));
        Assert.IsTrue(obs.All(o => o.Date == new DateOnly(2026, 6, 21)));
    }

    [TestMethod]
    public async Task ShopAsync_LogsBasketBuiltActivity()
    {
        SetupSearch("milk", Cand("semi", "Semi Skimmed 2.27L", 1.45m));
        var plan = Plan(new PlannedItem("milk", "milk", null, 1));

        await _hound.ShopAsync(plan, At, "run-1", CancellationToken.None);

        _logger.Verify(l => l.LogActivityAsync(
            It.Is<ActivityLog>(a =>
                a.HoundName == nameof(ShopperHound)
                && a.Metadata != null
                && (string)a.Metadata["event"] == "BasketBuilt"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ShopAsync_SkipsUnavailableItem_AndLogsWarning()
    {
        // Only an out-of-stock candidate exists, so nothing is added for this item.
        SetupSearch("truffle", Cand("oos", "Fresh Truffle", 25m, inStock: false));
        var plan = Plan(new PlannedItem("truffle", "truffle", null, 1));

        var outcome = await _hound.ShopAsync(plan, At, "run-1", CancellationToken.None);

        Assert.AreEqual(0, outcome.Basket.Lines.Count);
        _browser.Verify(b => b.AddToBasketAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        _logger.Verify(l => l.LogActivityAsync(
            It.Is<ActivityLog>(a => a.Metadata != null && (string)a.Metadata["event"] == "ItemUnavailable"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ShopAsync_AuthFailure_LogsBlocked_ButStillBuilds()
    {
        _browser.Setup(b => b.LoginAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(false, "no creds"));
        SetupSearch("milk", Cand("semi", "Semi Skimmed 2.27L", 1.45m));
        var plan = Plan(new PlannedItem("milk", "milk", null, 1));

        await _hound.ShopAsync(plan, At, "run-1", CancellationToken.None);

        _logger.Verify(l => l.LogActivityAsync(
            It.Is<ActivityLog>(a => a.Metadata != null && (string)a.Metadata["event"] == "ShopBlocked"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_AdvancesPhase_AndSetsBasket()
    {
        SetupSearch("milk", Cand("semi", "Semi Skimmed 2.27L", 1.45m));
        var state = GroceryGraphState.Initial() with { Plan = Plan(new PlannedItem("milk", "milk", null, 1)) };

        var next = await _hound.ExecuteAsync(state, CancellationToken.None);

        Assert.AreEqual(GroceryPhase.Shopping, next.Phase);
        Assert.AreEqual("shopper-hound", next.CurrentNode);
        Assert.IsNotNull(next.Basket);
        Assert.AreEqual(1, next.Basket!.Lines.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_NullPlan_ProducesEmptyBasket()
    {
        var next = await _hound.ExecuteAsync(GroceryGraphState.Initial(), CancellationToken.None);

        Assert.IsNotNull(next.Basket);
        Assert.AreEqual(0, next.Basket!.Lines.Count);
        _browser.Verify(b => b.AddToBasketAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
