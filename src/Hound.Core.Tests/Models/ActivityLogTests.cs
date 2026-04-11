using Hound.Core.Models;

namespace Hound.Core.Tests.Models;

[TestClass]
public sealed class ActivityLogTests
{
    [TestMethod]
    public void ActivityLog_DefaultSeverity_IsInfo()
    {
        var log = new ActivityLog();
        Assert.AreEqual(ActivitySeverity.Info, log.Severity);
    }

    [TestMethod]
    public void ActivityLog_DefaultId_IsEmpty()
    {
        var log = new ActivityLog();
        Assert.AreEqual(string.Empty, log.Id);
    }

    [TestMethod]
    public void ActivityLog_DefaultPackId_IsEmpty()
    {
        var log = new ActivityLog();
        Assert.AreEqual(string.Empty, log.PackId);
    }

    [TestMethod]
    public void ActivityLog_DefaultHoundId_IsEmpty()
    {
        var log = new ActivityLog();
        Assert.AreEqual(string.Empty, log.HoundId);
    }

    [TestMethod]
    public void ActivityLog_DefaultHoundName_IsEmpty()
    {
        var log = new ActivityLog();
        Assert.AreEqual(string.Empty, log.HoundName);
    }

    [TestMethod]
    public void ActivityLog_DefaultMessage_IsEmpty()
    {
        var log = new ActivityLog();
        Assert.AreEqual(string.Empty, log.Message);
    }

    [TestMethod]
    public void ActivityLog_DefaultTimestamp_IsRecentUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var log = new ActivityLog();
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.IsTrue(log.Timestamp >= before && log.Timestamp <= after);
    }

    [TestMethod]
    public void ActivityLog_DefaultMetadata_IsNull()
    {
        var log = new ActivityLog();
        Assert.IsNull(log.Metadata);
    }

    [TestMethod]
    public void ActivityLog_SetProperties_ReflectsValues()
    {
        var timestamp = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        var metadata = new Dictionary<string, object> { ["symbol"] = "AAPL", ["price"] = 185.5m };

        var log = new ActivityLog
        {
            Id = "log-001",
            PackId = "trading-pack",
            HoundId = "analysis-hound",
            HoundName = "AnalysisHound",
            Message = "Market analysis complete",
            Severity = ActivitySeverity.Success,
            Timestamp = timestamp,
            Metadata = metadata
        };

        Assert.AreEqual("log-001", log.Id);
        Assert.AreEqual("trading-pack", log.PackId);
        Assert.AreEqual("analysis-hound", log.HoundId);
        Assert.AreEqual("AnalysisHound", log.HoundName);
        Assert.AreEqual("Market analysis complete", log.Message);
        Assert.AreEqual(ActivitySeverity.Success, log.Severity);
        Assert.AreEqual(timestamp, log.Timestamp);
        Assert.AreEqual(2, log.Metadata!.Count);
    }

    [TestMethod]
    public void ActivitySeverity_AllValues_Defined()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(ActivitySeverity), ActivitySeverity.Info));
        Assert.IsTrue(Enum.IsDefined(typeof(ActivitySeverity), ActivitySeverity.Warning));
        Assert.IsTrue(Enum.IsDefined(typeof(ActivitySeverity), ActivitySeverity.Error));
        Assert.IsTrue(Enum.IsDefined(typeof(ActivitySeverity), ActivitySeverity.Success));
    }
}
