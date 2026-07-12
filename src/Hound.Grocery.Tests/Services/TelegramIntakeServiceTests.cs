using Hound.Grocery.Config;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class TelegramIntakeServiceTests
{
    private Mock<ITelegramClient> _telegram = null!;
    private Mock<IConciergeMessageHandler> _handler = null!;

    [TestInitialize]
    public void Setup()
    {
        _telegram = new Mock<ITelegramClient>();
        _handler = new Mock<IConciergeMessageHandler>();
    }

    private TelegramIntakeService CreateService(params long[] allowedChatIds)
    {
        var settings = Options.Create(new TelegramSettings
        {
            BotToken = "test-token",
            AllowedChatIds = [.. allowedChatIds],
        });
        return new TelegramIntakeService(_telegram.Object, _handler.Object, settings);
    }

    private static TelegramIncomingMessage Message(long chatId, string text = "add milk") =>
        new(ChatId: chatId, SenderId: 5, SenderName: "Carl", Text: text, MessageId: 1);

    [TestMethod]
    public async Task HandleMessage_UnauthorisedChat_IsIgnored()
    {
        var service = CreateService(allowedChatIds: 100);

        await service.HandleMessageAsync(Message(chatId: 999), default);

        _handler.Verify(
            h => h.HandleMessageAsync(It.IsAny<TelegramIncomingMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _telegram.Verify(
            t => t.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task HandleMessage_AuthorisedChat_DispatchesAndReplies()
    {
        var service = CreateService(allowedChatIds: 100);
        _handler
            .Setup(h => h.HandleMessageAsync(It.IsAny<TelegramIncomingMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConciergeReply("Added milk.", ListChanged: true));

        await service.HandleMessageAsync(Message(chatId: 100), default);

        _handler.Verify(
            h => h.HandleMessageAsync(It.Is<TelegramIncomingMessage>(m => m.ChatId == 100), It.IsAny<CancellationToken>()),
            Times.Once);
        _telegram.Verify(
            t => t.SendMessageAsync(100, "Added milk.", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task HandleMessage_EmptyReply_IsNotSent()
    {
        var service = CreateService(allowedChatIds: 100);
        _handler
            .Setup(h => h.HandleMessageAsync(It.IsAny<TelegramIncomingMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConciergeReply(string.Empty));

        await service.HandleMessageAsync(Message(chatId: 100), default);

        _telegram.Verify(
            t => t.SendMessageAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task HandleMessage_HandlerThrows_SendsApology()
    {
        var service = CreateService(allowedChatIds: 100);
        _handler
            .Setup(h => h.HandleMessageAsync(It.IsAny<TelegramIncomingMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await service.HandleMessageAsync(Message(chatId: 100), default);

        _telegram.Verify(
            t => t.SendMessageAsync(100, It.Is<string>(s => s.Contains("went wrong")), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
