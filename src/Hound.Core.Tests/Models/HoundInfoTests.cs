using Hound.Core.Models;

namespace Hound.Core.Tests.Models;

[TestClass]
public sealed class HoundInfoTests
{
    [TestMethod]
    public void HoundInfo_DefaultStatus_IsIdle()
    {
        var hound = new HoundInfo();
        Assert.AreEqual(HoundStatus.Idle, hound.Status);
    }

    [TestMethod]
    public void HoundInfo_DefaultId_IsEmpty()
    {
        var hound = new HoundInfo();
        Assert.AreEqual(string.Empty, hound.Id);
    }

    [TestMethod]
    public void HoundInfo_DefaultName_IsEmpty()
    {
        var hound = new HoundInfo();
        Assert.AreEqual(string.Empty, hound.Name);
    }

    [TestMethod]
    public void HoundInfo_DefaultPackId_IsEmpty()
    {
        var hound = new HoundInfo();
        Assert.AreEqual(string.Empty, hound.PackId);
    }

    [TestMethod]
    public void HoundInfo_DefaultLastActivity_IsNull()
    {
        var hound = new HoundInfo();
        Assert.IsNull(hound.LastActivity);
    }

    [TestMethod]
    public void HoundInfo_SetProperties_ReflectsValues()
    {
        var now = DateTime.UtcNow;
        var hound = new HoundInfo
        {
            Id = "analysis-hound",
            Name = "AnalysisHound",
            PackId = "trading-pack",
            Status = HoundStatus.Processing,
            LastActivity = now
        };

        Assert.AreEqual("analysis-hound", hound.Id);
        Assert.AreEqual("AnalysisHound", hound.Name);
        Assert.AreEqual("trading-pack", hound.PackId);
        Assert.AreEqual(HoundStatus.Processing, hound.Status);
        Assert.AreEqual(now, hound.LastActivity);
    }

    [TestMethod]
    public void HoundStatus_AllValues_Defined()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(HoundStatus), HoundStatus.Idle));
        Assert.IsTrue(Enum.IsDefined(typeof(HoundStatus), HoundStatus.Processing));
        Assert.IsTrue(Enum.IsDefined(typeof(HoundStatus), HoundStatus.Error));
        Assert.IsTrue(Enum.IsDefined(typeof(HoundStatus), HoundStatus.Disabled));
    }
}
