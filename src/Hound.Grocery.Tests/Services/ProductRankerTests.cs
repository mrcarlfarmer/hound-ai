using Hound.Grocery.Nodes;
using Hound.Grocery.Services;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class ProductRankerTests
{
    private readonly ProductRanker _ranker = new();

    private static ProductCandidate Candidate(
        string id,
        string name,
        decimal price,
        decimal? nectar = null,
        bool isNectar = false,
        bool favourite = false,
        bool inStock = true) =>
        new(id, name, price, nectar, isNectar, favourite, inStock, PerUnitPrice: null, Url: $"/groceries/product/{id}", ImgRef: null);

    private static PlannedItem Item(string raw, string? preferred = null, double qty = 1) =>
        new(raw, raw, preferred, qty);

    [TestMethod]
    public void Favourite_Beats_CheaperNonFavourite()
    {
        var result = _ranker.Rank(Item("milk"),
        [
            Candidate("cheap", "Budget Milk", 0.50m),
            Candidate("fav", "My Usual Milk", 1.50m, favourite: true),
        ]);

        Assert.IsNotNull(result.Chosen);
        Assert.AreEqual("fav", result.Chosen!.ProductId);
        Assert.IsFalse(result.IsSubstitution);
    }

    [TestMethod]
    public void Nectar_Beats_CheaperNonNectar_WhenNoFavourite()
    {
        var result = _ranker.Rank(Item("milk"),
        [
            Candidate("cheap", "Budget Milk", 0.80m),
            Candidate("nectar", "Loyalty Milk", 1.20m, nectar: 0.90m, isNectar: true),
        ]);

        Assert.AreEqual("nectar", result.Chosen!.ProductId);
    }

    [TestMethod]
    public void LowerEffectivePrice_Wins_WhenNeitherFavouriteNorNectar()
    {
        var result = _ranker.Rank(Item("milk"),
        [
            Candidate("a", "Milk A", 1.50m),
            Candidate("b", "Milk B", 1.10m),
        ]);

        Assert.AreEqual("b", result.Chosen!.ProductId);
    }

    [TestMethod]
    public void EffectivePrice_UsesNectarPrice_ForRanking()
    {
        // Both are Nectar; the one with the lower *nectar* price wins even though
        // its retail price is higher.
        var result = _ranker.Rank(Item("milk"),
        [
            Candidate("a", "Milk A", 1.00m, nectar: 0.95m, isNectar: true),
            Candidate("b", "Milk B", 1.50m, nectar: 0.80m, isNectar: true),
        ]);

        Assert.AreEqual("b", result.Chosen!.ProductId);
    }

    [TestMethod]
    public void PreferredProduct_Available_IsChosen_NotSubstitution()
    {
        var result = _ranker.Rank(Item("milk", preferred: "Semi Skimmed"),
        [
            Candidate("whole", "Whole Milk 2.27L", 1.40m, favourite: true),
            Candidate("semi", "Semi Skimmed Milk 2.27L", 1.45m),
        ]);

        Assert.AreEqual("semi", result.Chosen!.ProductId);
        Assert.IsFalse(result.IsSubstitution);
        Assert.IsNull(result.SubstitutionReason);
    }

    [TestMethod]
    public void PreferredProduct_Unavailable_SubstitutesClosest_WithReason()
    {
        var result = _ranker.Rank(Item("milk", preferred: "Oat Milk Barista"),
        [
            Candidate("semi", "Semi Skimmed Milk 2.27L", 1.45m, favourite: true),
            Candidate("whole", "Whole Milk 2.27L", 1.40m),
        ]);

        Assert.IsTrue(result.IsSubstitution);
        Assert.AreEqual("semi", result.Chosen!.ProductId);
        StringAssert.Contains(result.SubstitutionReason, "Oat Milk Barista");
        StringAssert.Contains(result.SubstitutionReason, "Semi Skimmed Milk 2.27L");
    }

    [TestMethod]
    public void OutOfStock_Candidates_AreExcluded()
    {
        var result = _ranker.Rank(Item("milk"),
        [
            Candidate("oos", "Cheap OOS Milk", 0.50m, favourite: true, inStock: false),
            Candidate("ok", "In Stock Milk", 1.20m),
        ]);

        Assert.AreEqual("ok", result.Chosen!.ProductId);
    }

    [TestMethod]
    public void NoInStockCandidates_YieldsNullChosen()
    {
        var result = _ranker.Rank(Item("milk"),
        [
            Candidate("oos", "OOS Milk", 1.00m, inStock: false),
        ]);

        Assert.IsNull(result.Chosen);
        Assert.IsFalse(result.IsSubstitution);
    }

    [TestMethod]
    public void EmptyCandidates_YieldsNullChosen()
    {
        var result = _ranker.Rank(Item("milk"), Array.Empty<ProductCandidate>());
        Assert.IsNull(result.Chosen);
        Assert.AreEqual(0, result.Ranked.Count);
    }

    [TestMethod]
    public void NoPreferredHint_PicksTopRanked_NotSubstitution()
    {
        var result = _ranker.Rank(Item("milk"),
        [
            Candidate("a", "Milk A", 1.50m),
            Candidate("b", "Milk B", 1.10m),
        ]);

        Assert.AreEqual("b", result.Chosen!.ProductId);
        Assert.IsFalse(result.IsSubstitution);
    }
}
