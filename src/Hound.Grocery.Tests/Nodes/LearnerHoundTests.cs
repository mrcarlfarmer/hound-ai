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
public class LearnerHoundTests
{
    private Mock<IActivityLogger> _logger = null!;
    private string _tempDir = null!;
    private StateFileService _stateFiles = null!;
    private PreferenceService _preferences = null!;
    private PurchaseHistoryService _history = null!;
    private PreferenceLearner _learner = null!;
    private LearnerHound _hound = null!;

    [TestInitialize]
    public void Setup()
    {
        _logger = new Mock<IActivityLogger>();
        _tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-tests", Guid.NewGuid().ToString("N"));
        _stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = _tempDir }));
        _preferences = new PreferenceService(_stateFiles);
        _history = new PurchaseHistoryService(_stateFiles);
        _learner = new PreferenceLearner(Options.Create(new LearnerSettings()));
        _hound = new LearnerHound(_logger.Object, _preferences, _history, _learner);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static PurchaseObservation Obs(string item, string product, double qty = 1, int day = 1) =>
        new(item, product, qty, new DateOnly(2026, 6, day));

    [TestMethod]
    public void NodeId_And_PackId_FollowConventions()
    {
        Assert.AreEqual("learner-hound", _hound.NodeId);
        Assert.AreEqual(GroceryPack.PackId, _hound.PackId);
    }

    [TestMethod]
    public async Task LearnAsync_PersistsPreferences_AndLogsPreferencesUpdated()
    {
        var result = await _hound.LearnAsync(
            [Obs("milk", "Semi Skimmed 2.27L", 2, 1), Obs("milk", "Semi Skimmed 2.27L", 2, 8)],
            [],
            CancellationToken.None);

        Assert.IsTrue(result.HasChanges);

        var doc = await _preferences.LoadAsync();
        Assert.IsTrue(doc.Mappings.Any(m => m.Item == "milk" && m.PreferredProduct == "Semi Skimmed 2.27L"));

        _logger.Verify(l => l.LogActivityAsync(
            It.Is<ActivityLog>(a =>
                a.HoundName == nameof(LearnerHound)
                && a.Metadata != null
                && (string)a.Metadata["event"] == "PreferencesUpdated"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task LearnAsync_DoesNotClobberExistingHumanEntry()
    {
        const string md = """
            # Preferences

            ## Product mappings

            - milk → "Human Choice Milk" · usual qty 3 · confidence 0.9 · favourite

            ## Dislikes
            """;
        await _stateFiles.WriteAsync(GroceryStateFile.Preferences, md);

        await _hound.LearnAsync([Obs("eggs", "Free Range Eggs")], [], CancellationToken.None);

        var doc = await _preferences.LoadAsync();
        var milk = doc.Mappings.Single(m => m.Item == "milk");
        Assert.AreEqual("Human Choice Milk", milk.PreferredProduct);
        Assert.AreEqual(3d, milk.UsualQuantity);
        Assert.IsTrue(milk.IsFavourite);
        Assert.IsTrue(doc.Mappings.Any(m => m.Item == "eggs"));
    }

    [TestMethod]
    public async Task ExecuteAsync_ReadsPurchaseHistory_AndAdvancesPhase()
    {
        await _history.AppendAsync(
        [
            Obs("milk", "Semi Skimmed 2.27L", 2, 1),
            Obs("milk", "Semi Skimmed 2.27L", 2, 8),
        ]);

        var next = await _hound.ExecuteAsync(GroceryGraphState.Initial(), CancellationToken.None);

        Assert.AreEqual(GroceryPhase.Learning, next.Phase);
        Assert.AreEqual("learner-hound", next.CurrentNode);

        var doc = await _preferences.LoadAsync();
        Assert.IsTrue(doc.Mappings.Any(m => m.Item == "milk"));
    }

    [TestMethod]
    public async Task LearnAsync_NoSignals_StillLogsWithNoChanges()
    {
        var result = await _hound.LearnAsync([], [], CancellationToken.None);

        Assert.IsFalse(result.HasChanges);
        _logger.Verify(l => l.LogActivityAsync(
            It.Is<ActivityLog>(a => a.Metadata != null && (int)a.Metadata["changeCount"] == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
