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
public class PlannerHoundTests
{
    private Mock<IActivityLogger> _logger = null!;
    private Mock<IItemMatcher> _matcher = null!;
    private Mock<IPlannerAssistant> _assistant = null!;
    private string _tempDir = null!;
    private StateFileService _stateFiles = null!;
    private ShoppingListService _shoppingList = null!;
    private PreferenceService _preferences = null!;
    private PlannerHound _hound = null!;

    [TestInitialize]
    public void Setup()
    {
        _logger = new Mock<IActivityLogger>();
        _matcher = new Mock<IItemMatcher>();
        _assistant = new Mock<IPlannerAssistant>();

        _tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-tests", Guid.NewGuid().ToString("N"));
        _stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = _tempDir }));
        _shoppingList = new ShoppingListService(_stateFiles);
        _preferences = new PreferenceService(_stateFiles);

        _hound = new PlannerHound(
            _logger.Object,
            _shoppingList,
            _preferences,
            _matcher.Object,
            _assistant.Object,
            Options.Create(new BudgetSettings { WeeklyTarget = 140m }));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private void DefaultAssistant() =>
        _assistant
            .Setup(a => a.SuggestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string item, CancellationToken _) => new PlanSuggestion(item, null, 1));

    [TestMethod]
    public void NodeId_And_PackId_FollowConventions()
    {
        Assert.AreEqual("planner-hound", _hound.NodeId);
        Assert.AreEqual("grocery-pack", _hound.PackId);
    }

    [TestMethod]
    public async Task Execute_EmptyList_ProducesEmptyPlan_AndLogsPlanBuilt()
    {
        var result = await _hound.ExecuteAsync(GroceryGraphState.Initial(), default);

        Assert.AreEqual(GroceryPhase.Planning, result.Phase);
        Assert.IsNotNull(result.Plan);
        Assert.AreEqual(0, result.Plan!.Items.Count);
        _logger.Verify(
            l => l.LogActivityAsync(
                It.Is<ActivityLog>(a => a.Message.Contains("PlanBuilt")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Execute_PropagatesWeeklyBudgetTarget()
    {
        var result = await _hound.ExecuteAsync(GroceryGraphState.Initial(), default);

        Assert.AreEqual(140m, result.Plan!.WeeklyBudgetTarget);
    }

    [TestMethod]
    public async Task ColdStart_NoPreferences_UsesAssistantSuggestion()
    {
        await _shoppingList.AddOrUpdateAsync("milk", 1, "Carl", DateTime.UtcNow.Date);
        _assistant
            .Setup(a => a.SuggestAsync("milk", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanSuggestion("semi skimmed milk", "Sainsbury's Semi Skimmed 2.27L", 2));

        var plan = await _hound.BuildPlanAsync(default);

        var item = plan.Items.Single();
        Assert.AreEqual("milk", item.RawListText);
        Assert.AreEqual("semi skimmed milk", item.SearchTerm);
        Assert.AreEqual("Sainsbury's Semi Skimmed 2.27L", item.PreferredProduct);
        Assert.AreEqual(2d, item.TargetQuantity);
        Assert.AreEqual(PlanPriority.Normal, item.Priority);
        Assert.IsFalse(item.IsFavourite);

        // With no learned keys the fuzzy matcher must not be consulted.
        _matcher.Verify(
            m => m.FindBestMatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ExactPreference_UsesProductAsSearchTerm_AndFavouriteRaisesPriority()
    {
        await _shoppingList.AddOrUpdateAsync("milk", 1, "Carl", DateTime.UtcNow.Date);
        await _stateFiles.WriteAsync(GroceryStateFile.Preferences, """
            ## Product mappings
            - milk → "Sainsbury's British Semi Skimmed Milk 2.27L" · usual qty 3 · confidence 0.9 · favourite
            """);
        DefaultAssistant();

        var plan = await _hound.BuildPlanAsync(default);

        var item = plan.Items.Single();
        Assert.AreEqual("Sainsbury's British Semi Skimmed Milk 2.27L", item.SearchTerm);
        Assert.AreEqual("Sainsbury's British Semi Skimmed Milk 2.27L", item.PreferredProduct);
        Assert.AreEqual(3d, item.TargetQuantity, "learned usual qty wins when the list qty is the default 1");
        Assert.IsTrue(item.IsFavourite);
        Assert.AreEqual(PlanPriority.High, item.Priority);

        _assistant.Verify(
            a => a.SuggestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ExplicitListQuantity_OverridesLearnedQuantity()
    {
        await _shoppingList.AddOrUpdateAsync("milk", 5, "Carl", DateTime.UtcNow.Date);
        await _stateFiles.WriteAsync(GroceryStateFile.Preferences, """
            ## Product mappings
            - milk → "Semi Skimmed 2.27L" · usual qty 3 · confidence 0.9
            """);
        DefaultAssistant();

        var plan = await _hound.BuildPlanAsync(default);

        Assert.AreEqual(5d, plan.Items.Single().TargetQuantity);
    }

    [TestMethod]
    public async Task FuzzyMatch_ResolvesToExistingPreference()
    {
        await _shoppingList.AddOrUpdateAsync("semi skimmed milk", 1, "Carl", DateTime.UtcNow.Date);
        await _stateFiles.WriteAsync(GroceryStateFile.Preferences, """
            ## Product mappings
            - milk → "Sainsbury's Semi Skimmed 2.27L" · usual qty 2 · confidence 0.8
            """);
        _matcher
            .Setup(m => m.FindBestMatchAsync("semi skimmed milk", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemMatch("milk", 0.9));
        DefaultAssistant();

        var plan = await _hound.BuildPlanAsync(default);

        var item = plan.Items.Single();
        Assert.AreEqual("Sainsbury's Semi Skimmed 2.27L", item.PreferredProduct);
        Assert.AreEqual(2d, item.TargetQuantity);
        _assistant.Verify(
            a => a.SuggestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task FuzzyMatch_NoMatch_FallsBackToAssistant()
    {
        await _shoppingList.AddOrUpdateAsync("dragon fruit", 1, "Carl", DateTime.UtcNow.Date);
        await _stateFiles.WriteAsync(GroceryStateFile.Preferences, """
            ## Product mappings
            - milk → "Semi Skimmed 2.27L" · usual qty 2 · confidence 0.8
            """);
        _matcher
            .Setup(m => m.FindBestMatchAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMatch?)null);
        _assistant
            .Setup(a => a.SuggestAsync("dragon fruit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanSuggestion("dragon fruit", "Fresh Dragon Fruit", 1));

        var plan = await _hound.BuildPlanAsync(default);

        Assert.AreEqual("Fresh Dragon Fruit", plan.Items.Single().PreferredProduct);
    }

    [TestMethod]
    public async Task BuildPlan_PreservesListOrder_ForMultipleItems()
    {
        await _shoppingList.AddOrUpdateAsync("milk", 1, "Carl", DateTime.UtcNow.Date);
        await _shoppingList.AddOrUpdateAsync("bread", 1, "Sam", DateTime.UtcNow.Date);
        DefaultAssistant();

        var plan = await _hound.BuildPlanAsync(default);

        Assert.AreEqual(2, plan.Items.Count);
        Assert.AreEqual("milk", plan.Items[0].RawListText);
        Assert.AreEqual("bread", plan.Items[1].RawListText);
    }

    [TestMethod]
    public void ResolveQuantity_FollowsPrecedence()
    {
        Assert.AreEqual(5d, PlannerHound.ResolveQuantity(listQuantity: 5, learned: 3, suggested: 2));
        Assert.AreEqual(3d, PlannerHound.ResolveQuantity(listQuantity: 1, learned: 3, suggested: 2));
        Assert.AreEqual(2d, PlannerHound.ResolveQuantity(listQuantity: 1, learned: null, suggested: 2));
        Assert.AreEqual(1d, PlannerHound.ResolveQuantity(listQuantity: 1, learned: null, suggested: null));
    }
}
