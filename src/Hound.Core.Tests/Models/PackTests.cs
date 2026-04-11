using Hound.Core.Models;

namespace Hound.Core.Tests.Models;

[TestClass]
public sealed class PackTests
{
    [TestMethod]
    public void Pack_DefaultStatus_IsIdle()
    {
        var pack = new Pack();
        Assert.AreEqual(PackStatus.Idle, pack.Status);
    }

    [TestMethod]
    public void Pack_DefaultId_IsEmpty()
    {
        var pack = new Pack();
        Assert.AreEqual(string.Empty, pack.Id);
    }

    [TestMethod]
    public void Pack_DefaultName_IsEmpty()
    {
        var pack = new Pack();
        Assert.AreEqual(string.Empty, pack.Name);
    }

    [TestMethod]
    public void Pack_DefaultHoundIds_IsEmptyList()
    {
        var pack = new Pack();
        Assert.IsNotNull(pack.HoundIds);
        Assert.AreEqual(0, pack.HoundIds.Count);
    }

    [TestMethod]
    public void Pack_DefaultLastActivity_IsNull()
    {
        var pack = new Pack();
        Assert.IsNull(pack.LastActivity);
    }

    [TestMethod]
    public void Pack_DefaultHoundCount_IsZero()
    {
        var pack = new Pack();
        Assert.AreEqual(0, pack.HoundCount);
    }

    [TestMethod]
    public void Pack_SetProperties_ReflectsValues()
    {
        var now = DateTime.UtcNow;
        var pack = new Pack
        {
            Id = "trading-pack",
            Name = "Trading Pack",
            Status = PackStatus.Running,
            HoundCount = 4,
            LastActivity = now,
            HoundIds = ["hound-1", "hound-2"]
        };

        Assert.AreEqual("trading-pack", pack.Id);
        Assert.AreEqual("Trading Pack", pack.Name);
        Assert.AreEqual(PackStatus.Running, pack.Status);
        Assert.AreEqual(4, pack.HoundCount);
        Assert.AreEqual(now, pack.LastActivity);
        Assert.AreEqual(2, pack.HoundIds.Count);
    }

    [TestMethod]
    public void PackStatus_AllValues_Defined()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(PackStatus), PackStatus.Idle));
        Assert.IsTrue(Enum.IsDefined(typeof(PackStatus), PackStatus.Running));
        Assert.IsTrue(Enum.IsDefined(typeof(PackStatus), PackStatus.Error));
        Assert.IsTrue(Enum.IsDefined(typeof(PackStatus), PackStatus.Stopped));
    }
}
