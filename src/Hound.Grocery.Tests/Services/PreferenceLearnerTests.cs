using Hound.Grocery.Config;
using Hound.Grocery.Nodes;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class PreferenceLearnerTests
{
    private static PreferenceLearner CreateLearner(
        double step = 0.2, double max = 1.0, double relearn = 0.5, int favourite = 3, int staple = 4) =>
        new(Options.Create(new LearnerSettings
        {
            ConfidenceStep = step,
            MaxConfidence = max,
            RelearnBelowConfidence = relearn,
            FavouriteThreshold = favourite,
            StapleThreshold = staple,
        }));

    private static PurchaseObservation Obs(string item, string product, double qty = 1, int day = 1) =>
        new(item, product, qty, new DateOnly(2026, 6, day));

    [TestMethod]
    public void Learn_FirstTime_FromEmpty_CreatesMappingWithEvidenceConfidence()
    {
        var learner = CreateLearner();

        var result = learner.Learn(
            PreferencesDocument.Empty,
            [Obs("milk", "Semi Skimmed 2.27L", 2, 1), Obs("milk", "Semi Skimmed 2.27L", 2, 8)],
            null);

        Assert.AreEqual(1, result.Document.Mappings.Count);
        var milk = result.Document.Mappings[0];
        Assert.AreEqual("milk", milk.Item);
        Assert.AreEqual("Semi Skimmed 2.27L", milk.PreferredProduct);
        Assert.AreEqual(0.4, milk.Confidence, 1e-9);
        Assert.AreEqual(2d, milk.UsualQuantity);
        Assert.IsFalse(milk.IsFavourite);
        Assert.IsTrue(result.Changes.Any(c => c.Contains("learned milk")));
    }

    [TestMethod]
    public void Learn_Reinforcement_RaisesConfidence()
    {
        var learner = CreateLearner();
        var existing = new PreferencesDocument(
            [new ProductPreference("milk", "Semi Skimmed 2.27L", 2, 0.4, false, false)], []);

        var result = learner.Learn(
            existing,
            [Obs("milk", "Semi Skimmed 2.27L", 2, 1), Obs("milk", "Semi Skimmed 2.27L", 2, 8)],
            null);

        var milk = result.Document.Mappings.Single();
        Assert.AreEqual("Semi Skimmed 2.27L", milk.PreferredProduct);
        Assert.AreEqual(0.8, milk.Confidence, 1e-9);
        Assert.IsTrue(result.Changes.Any(c => c.Contains("raised confidence for milk")));
    }

    [TestMethod]
    public void Learn_ConfidenceClampedToMax()
    {
        var learner = CreateLearner(step: 0.5, max: 1.0);
        var existing = new PreferencesDocument(
            [new ProductPreference("milk", "Semi Skimmed", 2, 0.8, false, false)], []);

        var result = learner.Learn(existing, [Obs("milk", "Semi Skimmed"), Obs("milk", "Semi Skimmed")], null);

        Assert.AreEqual(1.0, result.Document.Mappings.Single().Confidence, 1e-9);
    }

    [TestMethod]
    public void Learn_Quantity_IsRollingAverageWithPrior()
    {
        var learner = CreateLearner();
        var existing = new PreferencesDocument(
            [new ProductPreference("milk", "Semi Skimmed", 2, 0.6, false, false)], []);

        var result = learner.Learn(existing, [Obs("milk", "Semi Skimmed", 4)], null);

        // (2 + 4) / 2 = 3.
        Assert.AreEqual(3d, result.Document.Mappings.Single().UsualQuantity);
    }

    [TestMethod]
    public void Learn_PromotesToFavourite_AtThreshold()
    {
        var learner = CreateLearner(favourite: 3);

        var result = learner.Learn(
            PreferencesDocument.Empty,
            [Obs("eggs", "Free Range Eggs", 1, 1), Obs("eggs", "Free Range Eggs", 1, 8), Obs("eggs", "Free Range Eggs", 1, 15)],
            null);

        Assert.IsTrue(result.Document.Mappings.Single().IsFavourite);
        Assert.IsTrue(result.Changes.Any(c => c.Contains("promoted eggs to favourite")));
    }

    [TestMethod]
    public void Learn_PromotesToStaple_OnDistinctDates()
    {
        var learner = CreateLearner(staple: 4);

        var result = learner.Learn(
            PreferencesDocument.Empty,
            [Obs("bread", "Wholemeal", 1, 1), Obs("bread", "Wholemeal", 1, 8), Obs("bread", "Wholemeal", 1, 15), Obs("bread", "Wholemeal", 1, 22)],
            null);

        Assert.IsTrue(result.Document.Mappings.Single().IsStaple);
        Assert.IsTrue(result.Changes.Any(c => c.Contains("promoted bread to staple")));
    }

    [TestMethod]
    public void Learn_CapturesNewDislikes_DeduplicatedCaseInsensitive()
    {
        var learner = CreateLearner();
        var existing = new PreferencesDocument([], ["brand x crisps"]);

        var result = learner.Learn(existing, [], ["BRAND X CRISPS", "value ready meals"]);

        CollectionAssert.AreEqual(new[] { "brand x crisps", "value ready meals" }, result.Document.Dislikes.ToList());
        Assert.IsTrue(result.Changes.Any(c => c.Contains("added dislike: value ready meals")));
    }

    [TestMethod]
    public void Learn_NewDislike_ClearsMatchingPreferredProduct()
    {
        var learner = CreateLearner();
        var existing = new PreferencesDocument(
            [new ProductPreference("yoghurt", "Brand X Yoghurt 500g", 1, 0.8, false, false)], []);

        var result = learner.Learn(existing, [], ["Brand X"]);

        var yoghurt = result.Document.Mappings.Single();
        Assert.IsNull(yoghurt.PreferredProduct);
        Assert.AreEqual(0.0, yoghurt.Confidence, 1e-9);
        Assert.IsTrue(result.Changes.Any(c => c.Contains("cleared disliked product for yoghurt")));
    }

    [TestMethod]
    public void Learn_PreservesExistingEntries_NotInBatch()
    {
        var learner = CreateLearner();
        var existing = new PreferencesDocument(
            new[]
            {
                new ProductPreference("milk", "Semi Skimmed", 2, 0.8, IsFavourite: true, IsStaple: false),
                new ProductPreference("bread", "Wholemeal", 1, 0.6, false, true),
            },
            ["brand x"]);

        var result = learner.Learn(existing, [Obs("eggs", "Free Range Eggs")], null);

        // Human/learned entries survive untouched; the new item is appended.
        var milk = result.Document.Mappings.Single(m => m.Item == "milk");
        Assert.AreEqual("Semi Skimmed", milk.PreferredProduct);
        Assert.AreEqual(0.8, milk.Confidence, 1e-9);
        Assert.IsTrue(milk.IsFavourite);
        Assert.IsTrue(result.Document.Mappings.Any(m => m.Item == "bread"));
        Assert.IsTrue(result.Document.Mappings.Any(m => m.Item == "eggs"));
        CollectionAssert.Contains(result.Document.Dislikes.ToList(), "brand x");
    }

    [TestMethod]
    public void Learn_TrustedMapping_NotOverwrittenByDivergentEvidence()
    {
        var learner = CreateLearner(relearn: 0.5);
        var existing = new PreferencesDocument(
            [new ProductPreference("milk", "Semi Skimmed", 2, 0.8, false, false)], []);

        var result = learner.Learn(existing, [Obs("milk", "Whole Milk", 2), Obs("milk", "Whole Milk", 2)], null);

        var milk = result.Document.Mappings.Single();
        Assert.AreEqual("Semi Skimmed", milk.PreferredProduct);
        Assert.AreEqual(0.8, milk.Confidence, 1e-9);
    }

    [TestMethod]
    public void Learn_LowConfidenceMapping_IsRelearnedByNewEvidence()
    {
        var learner = CreateLearner(relearn: 0.5);
        var existing = new PreferencesDocument(
            [new ProductPreference("milk", "Semi Skimmed", 2, 0.3, false, false)], []);

        var result = learner.Learn(existing, [Obs("milk", "Oat Milk", 1), Obs("milk", "Oat Milk", 1)], null);

        var milk = result.Document.Mappings.Single();
        Assert.AreEqual("Oat Milk", milk.PreferredProduct);
        Assert.AreEqual(0.4, milk.Confidence, 1e-9);
        Assert.IsTrue(result.Changes.Any(c => c.Contains("remapped milk")));
    }

    [TestMethod]
    public void Learn_MostFrequentProduct_Wins()
    {
        var learner = CreateLearner();

        var result = learner.Learn(
            PreferencesDocument.Empty,
            [Obs("milk", "Oat Milk"), Obs("milk", "Semi Skimmed"), Obs("milk", "Semi Skimmed")],
            null);

        Assert.AreEqual("Semi Skimmed", result.Document.Mappings.Single().PreferredProduct);
    }

    [TestMethod]
    public void Learn_NoObservationsNoDislikes_ReportsNoChanges()
    {
        var learner = CreateLearner();
        var existing = new PreferencesDocument(
            [new ProductPreference("milk", "Semi Skimmed", 2, 0.8, false, false)], []);

        var result = learner.Learn(existing, [], null);

        Assert.IsFalse(result.HasChanges);
        Assert.AreEqual(1, result.Document.Mappings.Count);
    }
}
