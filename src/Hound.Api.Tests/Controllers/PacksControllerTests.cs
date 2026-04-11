using Hound.Api.Controllers;
using Hound.Api.Repositories;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Hound.Api.Tests.Controllers;

[TestClass]
public class PacksControllerTests
{
    private Mock<IPackRepository> _mockRepo = null!;
    private PacksController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<IPackRepository>();
        _controller = new PacksController(_mockRepo.Object);
    }

    [TestMethod]
    public async Task GetPacks_ReturnsOkWithPacks()
    {
        var packs = new List<Pack>
        {
            new() { Id = "packs/1", Name = "Trading", Status = PackStatus.Running, HoundCount = 4 }
        };
        _mockRepo.Setup(r => r.GetAllPacksAsync(default)).ReturnsAsync(packs);

        var result = await _controller.GetPacks(default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
        var value = ok.Value as IReadOnlyList<Pack>;
        Assert.IsNotNull(value);
        Assert.AreEqual(1, value.Count);
        Assert.AreEqual("Trading", value[0].Name);
    }

    [TestMethod]
    public async Task GetPacks_ReturnsEmptyList_WhenNoPacks()
    {
        _mockRepo.Setup(r => r.GetAllPacksAsync(default))
                 .ReturnsAsync(new List<Pack>());

        var result = await _controller.GetPacks(default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
    }

    [TestMethod]
    public async Task GetPack_WhenFound_ReturnsOkWithPack()
    {
        var pack = new Pack { Id = "packs/1", Name = "Trading", HoundCount = 4 };
        _mockRepo.Setup(r => r.GetPackAsync("packs/1", default)).ReturnsAsync(pack);

        var result = await _controller.GetPack("packs/1", default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
        var value = ok.Value as Pack;
        Assert.IsNotNull(value);
        Assert.AreEqual("packs/1", value.Id);
    }

    [TestMethod]
    public async Task GetPack_WhenNotFound_ReturnsNotFound()
    {
        _mockRepo.Setup(r => r.GetPackAsync("packs/missing", default))
                 .ReturnsAsync((Pack?)null);

        var result = await _controller.GetPack("packs/missing", default);

        Assert.IsInstanceOfType<NotFoundResult>(result.Result);
    }

    [TestMethod]
    public async Task GetHounds_ReturnsOkWithHounds()
    {
        var hounds = new List<HoundInfo>
        {
            new() { Id = "hounds/1", Name = "AnalysisHound", PackId = "packs/1", Status = HoundStatus.Idle },
            new() { Id = "hounds/2", Name = "RiskHound", PackId = "packs/1", Status = HoundStatus.Processing }
        };
        _mockRepo.Setup(r => r.GetHoundsAsync("packs/1", default)).ReturnsAsync(hounds);

        var result = await _controller.GetHounds("packs/1", default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
        var value = ok.Value as IReadOnlyList<HoundInfo>;
        Assert.IsNotNull(value);
        Assert.AreEqual(2, value.Count);
    }

    [TestMethod]
    public async Task GetHounds_ReturnsEmptyList_WhenNoHounds()
    {
        _mockRepo.Setup(r => r.GetHoundsAsync("packs/empty", default))
                 .ReturnsAsync(new List<HoundInfo>());

        var result = await _controller.GetHounds("packs/empty", default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
    }
}
