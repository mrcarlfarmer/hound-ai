using Hound.Grocery.Config;
using Hound.Grocery.Nodes;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class PurchaseHistoryServiceTests
{
    private string _tempDir = null!;
    private StateFileService _stateFiles = null!;
    private PurchaseHistoryService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-tests", Guid.NewGuid().ToString("N"));
        _stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = _tempDir }));
        _service = new PurchaseHistoryService(_stateFiles);
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
    public async Task Load_WhenMissing_ReturnsEmpty()
    {
        var observations = await _service.LoadAsync();

        Assert.AreEqual(0, observations.Count);
    }

    [TestMethod]
    public void Render_Then_Parse_RoundTrips_SkippingHeaderRows()
    {
        var original = new List<PurchaseObservation>
        {
            new("milk", "Semi Skimmed 2.27L", 2, new DateOnly(2026, 6, 21)),
            new("eggs", "Free Range Eggs", 1.5, new DateOnly(2026, 6, 21)),
        };

        var markdown = PurchaseHistoryService.Render(original);
        var parsed = PurchaseHistoryService.Parse(markdown);

        CollectionAssert.AreEqual(original, parsed.ToList());
    }

    [TestMethod]
    public async Task Append_AccumulatesAcrossCalls()
    {
        await _service.AppendAsync([new PurchaseObservation("milk", "Semi Skimmed", 2, new DateOnly(2026, 6, 21))]);
        await _service.AppendAsync([new PurchaseObservation("bread", "Wholemeal", 1, new DateOnly(2026, 6, 28))]);

        var all = await _service.LoadAsync();

        Assert.AreEqual(2, all.Count);
        Assert.AreEqual("milk", all[0].Item);
        Assert.AreEqual("bread", all[1].Item);
    }

    [TestMethod]
    public async Task Append_Empty_IsNoOp()
    {
        await _service.AppendAsync([]);

        Assert.IsFalse(File.Exists(_stateFiles.PathFor(GroceryStateFile.PurchaseHistory)));
    }
}
