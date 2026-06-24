namespace Hound.Grocery.Services;

/// <summary>
/// A single inbound text message received from Telegram, normalised to the
/// fields the pack cares about. Decouples the rest of the pack from the
/// <c>Telegram.Bot</c> SDK types so intake logic is fully unit-testable.
/// </summary>
public record TelegramIncomingMessage(
    long ChatId,
    long SenderId,
    string SenderName,
    string Text,
    int MessageId);

/// <summary>
/// Thin abstraction over the Telegram bot transport (spec §13). Long-polling,
/// no webhook. The concrete <see cref="TelegramBotClientAdapter"/> wraps
/// <c>Telegram.Bot</c>; tests substitute a fake.
/// </summary>
public interface ITelegramClient
{
    /// <summary>Sends a plain-text message to a chat.</summary>
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Begins long-polling for updates and invokes <paramref name="onMessage"/>
    /// for each inbound text message. Runs until <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    Task ReceiveAsync(
        Func<TelegramIncomingMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken);
}
