using Hound.Core.Models;

namespace Hound.Core.Tests.Models;

[TestClass]
public sealed class HoundMessageTests
{
    [TestMethod]
    public void HoundMessage_DefaultId_IsEmpty()
    {
        var msg = new HoundMessage();
        Assert.AreEqual(string.Empty, msg.Id);
    }

    [TestMethod]
    public void HoundMessage_DefaultHoundId_IsEmpty()
    {
        var msg = new HoundMessage();
        Assert.AreEqual(string.Empty, msg.HoundId);
    }

    [TestMethod]
    public void HoundMessage_DefaultContent_IsEmpty()
    {
        var msg = new HoundMessage();
        Assert.AreEqual(string.Empty, msg.Content);
    }

    [TestMethod]
    public void HoundMessage_DefaultTimestamp_IsRecentUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var msg = new HoundMessage();
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.IsTrue(msg.Timestamp >= before && msg.Timestamp <= after);
    }

    [TestMethod]
    public void HoundMessage_DefaultToolCallId_IsNull()
    {
        var msg = new HoundMessage();
        Assert.IsNull(msg.ToolCallId);
    }

    [TestMethod]
    public void HoundMessage_SetProperties_ReflectsValues()
    {
        var timestamp = new DateTime(2024, 6, 1, 9, 30, 0, DateTimeKind.Utc);
        var msg = new HoundMessage
        {
            Id = "msg-001",
            HoundId = "strategy-hound",
            Role = MessageRole.Assistant,
            Content = "Recommend BUY AAPL",
            Timestamp = timestamp,
            ToolCallId = "tool-call-42"
        };

        Assert.AreEqual("msg-001", msg.Id);
        Assert.AreEqual("strategy-hound", msg.HoundId);
        Assert.AreEqual(MessageRole.Assistant, msg.Role);
        Assert.AreEqual("Recommend BUY AAPL", msg.Content);
        Assert.AreEqual(timestamp, msg.Timestamp);
        Assert.AreEqual("tool-call-42", msg.ToolCallId);
    }

    [TestMethod]
    public void MessageRole_AllValues_Defined()
    {
        Assert.IsTrue(Enum.IsDefined(typeof(MessageRole), MessageRole.System));
        Assert.IsTrue(Enum.IsDefined(typeof(MessageRole), MessageRole.User));
        Assert.IsTrue(Enum.IsDefined(typeof(MessageRole), MessageRole.Assistant));
        Assert.IsTrue(Enum.IsDefined(typeof(MessageRole), MessageRole.Tool));
    }
}
