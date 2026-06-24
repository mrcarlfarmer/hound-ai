using Hound.Grocery.Config;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class PreferenceServiceTests
{
    private string _tempDir = null!;
    private StateFileService _stateFiles = null!;
    private PreferenceService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-tests", Guid.NewGuid().ToString("N"));
        _stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = _tempDir }));
        _service = new PreferenceService(_stateFiles);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task Load_WhenMissing_WritesTemplate_AndReturnsEmpty()
    {
        var doc = await _service.LoadAsync();

        Assert.AreEqual(0, doc.Mappings.Count);
        Assert.AreEqual(0, doc.Dislikes.Count);
        Assert.IsTrue(File.Exists(_stateFiles.PathFor(GroceryStateFile.Preferences)),
            "a template scaffold should be created on first load");
    }

    [TestMethod]
    public async Task Load_TemplateScaffold_ReparsesToEmpty()
    {
        await _service.LoadAsync();

        // Loading again parses the freshly written template; the commented
        // examples must NOT be parsed as real mappings/dislikes.
        var doc = await _service.LoadAsync();

        Assert.AreEqual(0, doc.Mappings.Count);
        Assert.AreEqual(0, doc.Dislikes.Count);
    }

    [TestMethod]
    public void ParseDocument_ReadsMapping_WithAllTags()
    {
        const string md = """
            # Preferences

            ## Product mappings

            - milk → "Sainsbury's British Semi Skimmed Milk 2.27L" · usual qty 2 · confidence 0.8 · favourite

            ## Dislikes

            - value-range ready meals
            """;

        var doc = PreferenceService.ParseDocument(md);

        Assert.AreEqual(1, doc.Mappings.Count);
        var milk = doc.Mappings[0];
        Assert.AreEqual("milk", milk.Item);
        Assert.AreEqual("Sainsbury's British Semi Skimmed Milk 2.27L", milk.PreferredProduct);
        Assert.AreEqual(2d, milk.UsualQuantity);
        Assert.AreEqual(0.8d, milk.Confidence);
        Assert.IsTrue(milk.IsFavourite);
        Assert.IsFalse(milk.IsStaple);

        Assert.AreEqual(1, doc.Dislikes.Count);
        Assert.AreEqual("value-range ready meals", doc.Dislikes[0]);
    }

    [TestMethod]
    public void ParseDocument_SupportsAsciiArrow_AndNoneProduct_AndStaple()
    {
        const string md = """
            ## Product mappings
            - bin bags -> (none) · confidence 0.3 · staple
            """;

        var doc = PreferenceService.ParseDocument(md);

        Assert.AreEqual(1, doc.Mappings.Count);
        Assert.IsNull(doc.Mappings[0].PreferredProduct);
        Assert.IsTrue(doc.Mappings[0].IsStaple);
        Assert.IsNull(doc.Mappings[0].UsualQuantity);
    }

    [TestMethod]
    public void RenderDocument_Then_ParseDocument_RoundTrips()
    {
        var original = new PreferencesDocument(
            new[]
            {
                new ProductPreference("milk", "Sainsbury's Semi Skimmed 2.27L", 2, 0.8, IsFavourite: true, IsStaple: false),
                new ProductPreference("bread", null, null, 0.4, IsFavourite: false, IsStaple: true),
            },
            new[] { "brand x crisps" });

        var markdown = PreferenceService.RenderDocument(original);
        var parsed = PreferenceService.ParseDocument(markdown);

        Assert.AreEqual(2, parsed.Mappings.Count);
        CollectionAssert.AreEqual(original.Mappings.ToList(), parsed.Mappings.ToList());
        CollectionAssert.AreEqual(original.Dislikes.ToList(), parsed.Dislikes.ToList());
    }

    [TestMethod]
    public async Task Resolve_IsCaseInsensitive()
    {
        const string md = """
            ## Product mappings
            - Milk → "Semi Skimmed 2.27L" · usual qty 2 · confidence 0.8
            """;
        await _stateFiles.WriteAsync(GroceryStateFile.Preferences, md);

        var pref = await _service.ResolveAsync("MILK");

        Assert.IsNotNull(pref);
        Assert.AreEqual("Semi Skimmed 2.27L", pref!.PreferredProduct);
    }

    [TestMethod]
    public async Task Resolve_Unknown_ReturnsNull()
    {
        var pref = await _service.ResolveAsync("caviar");

        Assert.IsNull(pref);
    }
}
