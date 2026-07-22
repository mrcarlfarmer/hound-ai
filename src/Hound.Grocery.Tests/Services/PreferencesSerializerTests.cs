using Hound.Grocery.Services;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class PreferencesSerializerTests
{
    [TestMethod]
    public void Render_Then_Parse_IsLosslessRoundTrip()
    {
        var original = new PreferencesDocument(
            new[]
            {
                new ProductPreference("milk", "Sainsbury's Semi Skimmed 2.27L", 2, 0.8, IsFavourite: true, IsStaple: false),
                new ProductPreference("bread", null, null, 0.4, IsFavourite: false, IsStaple: true),
                new ProductPreference("eggs", "Free Range Eggs", 1.5, 0.6, IsFavourite: false, IsStaple: false),
            },
            new[] { "brand x crisps", "value ready meals" });

        var markdown = PreferencesSerializer.Render(original);
        var parsed = PreferencesSerializer.Parse(markdown);

        CollectionAssert.AreEqual(original.Mappings.ToList(), parsed.Mappings.ToList());
        CollectionAssert.AreEqual(original.Dislikes.ToList(), parsed.Dislikes.ToList());
    }

    [TestMethod]
    public void Parse_Then_Render_Then_Parse_IsStable()
    {
        const string md = """
            # Preferences

            ## Product mappings

            - milk → "Semi Skimmed 2.27L" · usual qty 2 · confidence 0.8 · favourite
            - bin bags -> (none) · confidence 0.3 · staple

            ## Dislikes

            - value-range ready meals
            """;

        var first = PreferencesSerializer.Parse(md);
        var second = PreferencesSerializer.Parse(PreferencesSerializer.Render(first));

        CollectionAssert.AreEqual(first.Mappings.ToList(), second.Mappings.ToList());
        CollectionAssert.AreEqual(first.Dislikes.ToList(), second.Dislikes.ToList());
    }

    [TestMethod]
    public void Template_ReparsesToEmpty()
    {
        var doc = PreferencesSerializer.Parse(PreferencesSerializer.Template());

        Assert.AreEqual(0, doc.Mappings.Count);
        Assert.AreEqual(0, doc.Dislikes.Count);
    }
}
