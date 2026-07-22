using Hound.Grocery.Config;
using Hound.Grocery.Nodes;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class ShoppingListServiceTests
{
    private string _tempDir = null!;
    private ShoppingListService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-tests", Guid.NewGuid().ToString("N"));
        var stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = _tempDir }));
        _service = new ShoppingListService(stateFiles);
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
    public async Task GetItems_WhenFileMissing_ReturnsEmpty()
    {
        var items = await _service.GetItemsAsync();

        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public async Task AddOrUpdate_NewItem_PersistsNameQuantityAndAttribution()
    {
        var added = await _service.AddOrUpdateAsync("milk", 2, "Carl", new DateTime(2026, 6, 21));

        Assert.AreEqual("milk", added.Name);
        Assert.AreEqual(2d, added.Quantity);

        var items = await _service.GetItemsAsync();
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("milk", items[0].Name);
        Assert.AreEqual(2d, items[0].Quantity);
        Assert.AreEqual("Carl", items[0].AddedBy);
        Assert.AreEqual(new DateTime(2026, 6, 21), items[0].AddedAt);
    }

    [TestMethod]
    public async Task AddOrUpdate_ExistingItem_IsCaseInsensitive_AndReplacesQuantity()
    {
        await _service.AddOrUpdateAsync("Milk", 2, "Carl", new DateTime(2026, 6, 21));
        await _service.AddOrUpdateAsync("milk", 5, "Sam", new DateTime(2026, 6, 22));

        var items = await _service.GetItemsAsync();
        Assert.AreEqual(1, items.Count, "case-insensitive name should update the existing item, not duplicate");
        Assert.AreEqual(5d, items[0].Quantity);
        Assert.AreEqual("Sam", items[0].AddedBy);
    }

    [TestMethod]
    public async Task Remove_ExistingItem_ReturnsTrue_AndDeletes()
    {
        await _service.AddOrUpdateAsync("eggs", 1, "Carl", DateTime.UtcNow.Date);

        var removed = await _service.RemoveAsync("EGGS");

        Assert.IsTrue(removed);
        Assert.AreEqual(0, (await _service.GetItemsAsync()).Count);
    }

    [TestMethod]
    public async Task Remove_MissingItem_ReturnsFalse()
    {
        var removed = await _service.RemoveAsync("ghost");

        Assert.IsFalse(removed);
    }

    [TestMethod]
    public async Task SetQuantity_ExistingItem_UpdatesQuantity_KeepsAttribution()
    {
        await _service.AddOrUpdateAsync("bananas", 2, "Carl", new DateTime(2026, 6, 21));

        var updated = await _service.SetQuantityAsync("bananas", 6, "Sam", new DateTime(2026, 6, 22));

        Assert.AreEqual(6d, updated.Quantity);
        var items = await _service.GetItemsAsync();
        Assert.AreEqual(6d, items[0].Quantity);
        Assert.AreEqual("Carl", items[0].AddedBy, "set-quantity should preserve the original adder");
    }

    [TestMethod]
    public async Task SetQuantity_MissingItem_AddsIt()
    {
        var item = await _service.SetQuantityAsync("apples", 3, "Carl", new DateTime(2026, 6, 21));

        Assert.AreEqual("apples", item.Name);
        Assert.AreEqual(3d, item.Quantity);
        Assert.AreEqual(1, (await _service.GetItemsAsync()).Count);
    }

    [TestMethod]
    public async Task FractionalQuantity_RoundTrips()
    {
        await _service.AddOrUpdateAsync("cheese", 0.5, "Carl", new DateTime(2026, 6, 21));

        var items = await _service.GetItemsAsync();
        Assert.AreEqual(0.5d, items[0].Quantity);
    }

    [TestMethod]
    public async Task RenderSummary_Empty_SaysEmpty()
    {
        var summary = await _service.RenderSummaryAsync();

        StringAssert.Contains(summary, "empty");
    }

    [TestMethod]
    public async Task RenderSummary_ListsEveryItem()
    {
        await _service.AddOrUpdateAsync("milk", 2, "Carl", DateTime.UtcNow.Date);
        await _service.AddOrUpdateAsync("bread", 1, "Sam", DateTime.UtcNow.Date);

        var summary = await _service.RenderSummaryAsync();

        StringAssert.Contains(summary, "milk");
        StringAssert.Contains(summary, "bread");
    }

    [TestMethod]
    public void RenderMarkdown_Then_ParseItems_RoundTrips()
    {
        var original = new List<ShoppingListItem>
        {
            new("milk", 2, "Carl", new DateTime(2026, 6, 21)),
            new("cheese", 0.5, "Sam", new DateTime(2026, 6, 22)),
        };

        var markdown = ShoppingListService.RenderMarkdown(original);
        var parsed = ShoppingListService.ParseItems(markdown);

        Assert.AreEqual(2, parsed.Count);
        CollectionAssert.AreEqual(original, parsed);
    }
}
