namespace Hound.Grocery.Services;

/// <summary>
/// The reply ConciergeHound produces for one inbound message: the text to send
/// back over Telegram and whether the shopping list was changed.
/// </summary>
public record ConciergeReply(string Text, bool ListChanged = false);

/// <summary>
/// Handles a single authorised inbound Telegram message and returns the reply
/// to send. Implemented by <see cref="Hound.Grocery.Nodes.ConciergeHound"/>;
/// the Telegram intake service depends on this abstraction so dispatch is
/// unit-testable.
/// </summary>
public interface IConciergeMessageHandler
{
    Task<ConciergeReply> HandleMessageAsync(TelegramIncomingMessage message, CancellationToken cancellationToken);
}
