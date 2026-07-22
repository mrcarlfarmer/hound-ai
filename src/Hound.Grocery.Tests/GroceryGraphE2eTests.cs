using System.Reflection;
using Hound.Core.Logging;
using Hound.Grocery.Config;
using Hound.Grocery.Graph;
using Hound.Grocery.Nodes;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace Hound.Grocery.Tests;

/// <summary>
/// End-to-end dry run of the full grocery graph:
/// loose list -> PlannerHound -> ShopperHound (mocked sidecar) -> BudgetHound -> LearnerHound.
/// The LLM, embedding and browser seams are mocked; all state services run on a temp
/// directory. Asserts the basket builds, BudgetHound reconciles the real subtotal, the
/// learning loop persists, and R1 (never a slot/checkout/pay) is never breached.
/// </summary>
[TestClass]
public class GroceryGraphE2eTests
{
    private string _tempDir = null!;
    private StateFileService _stateFiles = null!;
    private Mock<IActivityLogger> _logger = null!;
    private Mock<IBrowserWorkerClient> _browser = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-e2e", Guid.NewGuid().ToString("N"));
        _stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = _tempDir }));
        _logger = new Mock<IActivityLogger>();
        _browser = new Mock<IBrowserWorkerClient>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ProductCandidate Cand(string id, string name, decimal price, bool favourite = false) =>
        new(id, name, price, null, false, favourite, true, null, $"/groceries/product/{id}", null);

    [TestMethod]
    public async Task FullGraph_LooseList_BuildsBasket_Reconciles_Learns_NeverBreachesR1()
    {
        // ── Arrange: a loose shopping list on disk (cold-start, empty preferences) ──
        var shoppingList = new ShoppingListService(_stateFiles);
        await shoppingList.AddOrUpdateAsync("milk", 2, "carl", new DateTime(2026, 6, 1));
        await shoppingList.AddOrUpdateAsync("eggs", 1, "carl", new DateTime(2026, 6, 1));

        var preferences = new PreferenceService(_stateFiles);

        // LLM + embedding seams: matcher finds nothing (cold start), assistant echoes the item.
        var matcher = new Mock<IItemMatcher>();
        matcher.Setup(m => m.FindBestMatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMatch?)null);
        var assistant = new Mock<IPlannerAssistant>();
        assistant.Setup(a => a.SuggestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string term, CancellationToken _) => new PlanSuggestion(term, null, 1));

        var planner = new PlannerHound(
            _logger.Object, shoppingList, preferences, matcher.Object, assistant.Object,
            Options.Create(new BudgetSettings { WeeklyTarget = 140m }));

        // Browser sidecar (mocked, DRY_RUN semantics — empty snapshots => synthesised basket).
        _browser.Setup(b => b.LoginAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult(true, "ok"));
        _browser.Setup(b => b.SearchAsync("milk", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResult("milk", new[] { Cand("semi", "Semi Skimmed 2.27L", 1.45m, favourite: true) }));
        _browser.Setup(b => b.SearchAsync("eggs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchResult("eggs", new[] { Cand("eggs6", "Free Range Eggs x6", 1.20m) }));
        _browser.Setup(b => b.AddToBasketAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasketSnapshot(Array.Empty<BasketLine>(), 0m));
        _browser.Setup(b => b.GetBasketAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BasketSnapshot(Array.Empty<BasketLine>(), 0m));

        var shopper = new ShopperHound(
            _logger.Object, _browser.Object, new ProductRanker(),
            new BasketTraceService(_stateFiles), new PurchaseHistoryService(_stateFiles));

        var budget = new BudgetHound(
            _logger.Object,
            new BudgetLedgerService(Options.Create(new BudgetSettings { WeeklyTarget = 140m }), _stateFiles));

        var learner = new LearnerHound(
            _logger.Object, preferences, new PurchaseHistoryService(_stateFiles),
            new PreferenceLearner(Options.Create(new LearnerSettings())));

        // ── Act: run the graph end to end ──
        var state = GroceryGraphState.Initial();
        state = await planner.ExecuteAsync(state, CancellationToken.None);
        state = await shopper.ExecuteAsync(state, CancellationToken.None);
        state = await budget.ExecuteAsync(state, CancellationToken.None);
        state = await learner.ExecuteAsync(state, CancellationToken.None);

        // ── Assert: plan built from the loose list ──
        Assert.IsNotNull(state.Plan);
        Assert.AreEqual(2, state.Plan!.Items.Count);

        // ── Assert: basket built ──
        Assert.IsNotNull(state.Basket);
        Assert.AreEqual(2, state.Basket!.Lines.Count);
        var expectedSubtotal = (1.45m * 2) + (1.20m * 1);
        Assert.AreEqual(expectedSubtotal, state.Basket.Subtotal);

        // ── Assert: BudgetHound reconciled the real basket subtotal ──
        Assert.IsNotNull(state.BudgetVerdict);
        Assert.AreEqual(expectedSubtotal, state.BudgetVerdict!.BasketSubtotal);

        // ── Assert: the learning loop persisted purchase observations + preferences ──
        var history = await new PurchaseHistoryService(_stateFiles).LoadAsync();
        Assert.AreEqual(2, history.Count);
        var prefsDoc = await preferences.LoadAsync();
        Assert.IsTrue(prefsDoc.Mappings.Any(m => m.Item == "milk"));

        // ── Assert R1: only allowlisted browser operations were ever requested ──
        _browser.Verify(b => b.LoginAsync(It.IsAny<CancellationToken>()), Times.Once);
        _browser.Verify(b => b.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _browser.Verify(b => b.AddToBasketAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _browser.Verify(b => b.GetBasketAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _browser.VerifyNoOtherCalls();

        // ── Assert R1 (structural): the browser contract exposes no slot/checkout/pay method ──
        foreach (var method in typeof(IBrowserWorkerClient).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = method.Name.ToLowerInvariant();
            Assert.IsFalse(
                name.Contains("checkout") || name.Contains("slot") ||
                name.Contains("pay") || name.Contains("book"),
                $"R1 violation: IBrowserWorkerClient must not expose '{method.Name}'.");
        }
    }
}
