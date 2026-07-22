using Hound.Api.Repositories;
using Hound.Core.Models;
using Raven.Client.Documents;
using Raven.Embedded;

namespace Hound.Api.Tests.Repositories;

/// <summary>
/// Integration tests for <see cref="RavenDebateRepository"/> that exercise the
/// real RavenDB document-loading pipeline against an embedded server, verifying that
/// persisted <see cref="DebateRecord"/> documents are filtered by run id and
/// returned in refinement order.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RavenDebateRepositoryTests
{
    private const string Database = "hound-trading-pack";

    private static IDocumentStore _store = null!;
    private static string _dataDirectory = string.Empty;
    private static bool _serverStarted;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _dataDirectory = Path.Combine(Path.GetTempPath(), "hound-raven-tests", Guid.NewGuid().ToString("N"));
        EmbeddedServer.Instance.StartServer(new ServerOptions
        {
            DataDirectory = _dataDirectory,
            FrameworkVersion = "8.0.0+",
        });
        _store = EmbeddedServer.Instance.GetDocumentStore(Database);
        _serverStarted = true;
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _store?.Dispose();
        if (_serverStarted)
            EmbeddedServer.Instance.Dispose();

        // The embedded server holds the data directory until it is disposed;
        // remove it now so test runs don't leave RavenDB data behind in temp.
        try
        {
            if (!string.IsNullOrEmpty(_dataDirectory) && Directory.Exists(_dataDirectory))
                Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup — a lingering file handle should not fail the run.
        }
    }

    private static async Task SeedAsync(params DebateRecord[] records)
    {
        using var session = _store.OpenAsyncSession(Database);
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

    [TestMethod]
    public async Task GetDebatesAsync_WithMoreThanOnePage_ReturnsEveryRecord()
    {
        var runId = "run-paged-" + Guid.NewGuid().ToString("N");
        var records = Enumerable.Range(0, 130)
            .Select(refinement => Record(runId, refinement))
            .ToArray();
        await SeedAsync(records);

        var repo = new RavenDebateRepository(_store);
        var result = await repo.GetDebatesAsync(runId);

        Assert.AreEqual(130, result.Count);
        Assert.AreEqual(0, result[0].RefinementCount);
        Assert.AreEqual(129, result[^1].RefinementCount);
    }
}
