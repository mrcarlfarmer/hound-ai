using Hound.Grocery.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Hound.Grocery.Services;

/// <summary>
/// <see cref="ITelegramClient"/> implementation over the <c>Telegram.Bot</c> SDK
/// using long-polling <c>getUpdates</c> (spec §13: no webhook, no inbound port).
/// <para>
/// The underlying <see cref="TelegramBotClient"/> is created lazily so the pack
/// can boot without a token (the intake service stays disabled until one is
/// configured). Construction never touches the network.
/// </para>
/// </summary>
public class TelegramBotClientAdapter : ITelegramClient
{
    private readonly TelegramSettings _settings;
    private readonly ILogger<TelegramBotClientAdapter>? _logger;
    private readonly Lazy<ITelegramBotClient> _bot;

    public TelegramBotClientAdapter(IOptions<TelegramSettings> options, ILoggerFactory? loggerFactory = null)
    {
        _settings = options.Value;
        _logger = loggerFactory?.CreateLogger<TelegramBotClientAdapter>();
        _bot = new Lazy<ITelegramBotClient>(CreateBotClient);
    }

    private ITelegramBotClient CreateBotClient()
    {
        if (string.IsNullOrWhiteSpace(_settings.BotToken))
        {
            throw new InvalidOperationException(
                "Telegram bot token is not configured (Telegram:BotToken). " +
                "Set it via env/secrets before using the Telegram client.");
        }

        return new TelegramBotClient(_settings.BotToken);
    }

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken) =>
        _bot.Value.SendMessage(chatId, text, cancellationToken: cancellationToken);

    public async Task ReceiveAsync(
        Func<TelegramIncomingMessage, CancellationToken, Task> onMessage,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        var allowedUpdates = new[] { UpdateType.Message };

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _bot.Value.GetUpdates(
                    offset,
                    limit: null,
                    timeout: 30,
                    allowedUpdates: allowedUpdates,
                    cancellationToken: cancellationToken);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;

                    var message = update.Message;
                    if (message?.Text is not { Length: > 0 } text)
                    {
                        continue;
                    }

                    var incoming = new TelegramIncomingMessage(
                        ChatId: message.Chat.Id,
                        SenderId: message.From?.Id ?? 0,
                        SenderName: ResolveSenderName(message.From),
                        Text: text,
                        MessageId: message.MessageId);

                    await onMessage(incoming, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Telegram getUpdates failed; backing off before retry.");
                await DelaySafelyAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private static string ResolveSenderName(Telegram.Bot.Types.User? user)
    {
        if (user is null)
        {
            return "unknown";
        }

        if (!string.IsNullOrWhiteSpace(user.Username))
        {
            return user.Username;
        }

        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }

    private static async Task DelaySafelyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down — nothing to do.
        }
    }
}
