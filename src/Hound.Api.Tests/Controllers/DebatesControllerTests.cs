using Hound.Api.Controllers;
using Hound.Api.Repositories;
using Hound.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Hound.Api.Tests.Controllers;

[TestClass]
public sealed class DebatesControllerTests
{
    private Mock<IDebateRepository> _mockRepo = null!;
    private DebatesController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<IDebateRepository>();
        _controller = new DebatesController(_mockRepo.Object);
    }

    [TestMethod]
    public async Task GetDebates_ReturnsOkWithRecords()
    {
        var records = new List<DebateRecord>
        {
            new() { Id = "DebateRecords/run-1/0", RunId = "run-1", Symbol = "AAPL", RefinementCount = 0 },
        };
        _mockRepo
            .Setup(r => r.GetDebatesAsync("run-1", default))
            .ReturnsAsync(records);

        var result = await _controller.GetDebates("run-1", default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreEqual(200, ok.StatusCode);
        var payload = ok.Value as IEnumerable<DebateRecord>;
        Assert.IsNotNull(payload);
        Assert.AreEqual(1, payload.Count());
    }

    [TestMethod]
    public async Task GetDebates_ReturnsOkWithEmptyList_WhenNoRecords()
    {
        _mockRepo
            .Setup(r => r.GetDebatesAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new List<DebateRecord>());

        var result = await _controller.GetDebates("run-missing", default);

        var ok = result.Result as OkObjectResult;
        Assert.IsNotNull(ok);
        var payload = ok.Value as IEnumerable<DebateRecord>;
        Assert.IsNotNull(payload);
        Assert.AreEqual(0, payload.Count());
    }
}
