using Hound.Api.Repositories;
using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Embedded;

namespace Hound.Api.Tests.Repositories;

/// <summary>
/// Integration tests for <see cref="RavenDebateRepository"/> that exercise the
/// real RavenDB query pipeline against an embedded server, verifying that
/// persisted <see cref="DebateRecord"/> documents are filtered by run id and
/// returned in refinement order.
/// </summary>
[TestClass]
public sealed class RavenDebateRepositoryTests
{
    private const string Database = "hound-trading-pack";

    private static IDocumentStore _store = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        EmbeddedServer.Instance.StartServer(new ServerOptions
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "hound-raven-tests", Guid.NewGuid().ToString("N")),
        });
        _store = EmbeddedServer.Instance.GetDocumentStore(Database);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _store?.Dispose();
        EmbeddedServer.Instance.Dispose();
    }

    private static async Task SeedAsync(params DebateRecord[] records)
    {
        using var session = _store.OpenAsyncSession(Database);
        // Wait for the auto-index to catch up so subsequent queries are not stale.
        session.Advanced.WaitForIndexesAfterSaveChanges();
        foreach (var record in records)
        {
            await session.StoreAsync(record, record.Id);
        }
        await session.SaveChangesAsync();
    }

    private static DebateRecord Record(string runId, int refinement, string symbol = "AAPL") => new()
    {
        Id = $"DebateRecords/{runId}/{refinement}",
        RunId = runId,
        Symbol = symbol,
        RefinementCount = refinement,
        TurnsPerSide = 1,
        CreatedAt = DateTime.UtcNow,
        Turns =
        [
            new DebateTurn("Bull", 0, $"bull-{runId}-{refinement}", DateTime.UtcNow),
            new DebateTurn("Bear", 1, $"bear-{runId}-{refinement}", DateTime.UtcNow),
        ],
    };

    [TestMethod]
    public async Task GetDebatesAsync_ReturnsOnlyRecordsForTheRequestedRun()
    {
        var runId = "run-filter-" + Guid.NewGuid().ToString("N");
        var otherRunId = "run-other-" + Guid.NewGuid().ToString("N");
        await SeedAsync(Record(runId, 0), Record(otherRunId, 0));

        var repo = new RavenDebateRepository(_store);
        var result = await repo.GetDebatesAsync(runId);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(runId, result[0].RunId);
        Assert.AreEqual(2, result[0].Turns.Count);
        Assert.AreEqual("Bull", result[0].Turns[0].Role);
    }

    [TestMethod]
    public async Task GetDebatesAsync_OrdersRecordsByRefinementCount()
    {
        var runId = "run-order-" + Guid.NewGuid().ToString("N");
        // Seed out of order to prove the repository sorts.
        await SeedAsync(Record(runId, 2), Record(runId, 0), Record(runId, 1));

        var repo = new RavenDebateRepository(_store);
        var result = await repo.GetDebatesAsync(runId);

        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 },
            result.Select(r => r.RefinementCount).ToArray());
    }

    [TestMethod]
    public async Task GetDebatesAsync_ReturnsEmpty_WhenRunHasNoDebates()
    {
        var repo = new RavenDebateRepository(_store);
        var result = await repo.GetDebatesAsync("run-missing-" + Guid.NewGuid().ToString("N"));

        Assert.AreEqual(0, result.Count);
    }
}
