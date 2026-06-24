using Hound.Grocery.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Services;

/// <summary>
/// Hosted service that owns the Telegram long-poll intake loop (spec §7.1, §13).
/// It authorises each inbound message against <see cref="TelegramSettings.AllowedChatIds"/>,
/// dispatches authorised messages to <see cref="IConciergeMessageHandler"/>, and
/// sends the reply back. Messages from any other chat are ignored.
/// <para>
/// When no bot token is configured the service stays dormant, so the pack can
/// boot (and the rest of the graph run) without Telegram credentials.
/// </para>
/// </summary>
public class TelegramIntakeService : BackgroundService
{
    private readonly ITelegramClient _telegram;
    private readonly IConciergeMessageHandler _handler;
    private readonly TelegramSettings _settings;
    private readonly HashSet<long> _allowedChatIds;
    private readonly ILogger<TelegramIntakeService>? _logger;

    public TelegramIntakeService(
        ITelegramClient telegram,
        IConciergeMessageHandler handler,
        IOptions<TelegramSettings> options,
        ILogger<TelegramIntakeService>? logger = null)
    {
        _telegram = telegram;
        _handler = handler;
        _settings = options.Value;
        _allowedChatIds = [.. _settings.AllowedChatIds];
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.BotToken))
        {
            _logger?.LogWarning("Telegram intake disabled: no bot token configured (Telegram:BotToken).");
            return;
        }

        if (_allowedChatIds.Count == 0)
        {
            _logger?.LogWarning(
                "Telegram intake started but no AllowedChatIds are configured — all messages will be ignored.");
        }

        _logger?.LogInformation(
            "Telegram intake started ({Count} authorised chat id(s)).", _allowedChatIds.Count);

        await _telegram.ReceiveAsync(HandleMessageAsync, stoppingToken);
    }

    internal async Task HandleMessageAsync(TelegramIncomingMessage message, CancellationToken cancellationToken)
    {
        if (!_allowedChatIds.Contains(message.ChatId))
        {
            _logger?.LogDebug("Ignoring message from unauthorised chat {ChatId}.", message.ChatId);
            return;
        }

        try
        {
            var reply = await _handler.HandleMessageAsync(message, cancellationToken);
            if (!string.IsNullOrWhiteSpace(reply.Text))
            {
                await _telegram.SendMessageAsync(message.ChatId, reply.Text, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Failed to handle Telegram message from chat {ChatId}.", message.ChatId);
            await TrySendAsync(message.ChatId, "Sorry — something went wrong handling that. Please try again.", cancellationToken);
        }
    }

    private async Task TrySendAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        try
        {
            await _telegram.SendMessageAsync(chatId, text, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send error reply to chat {ChatId}.", chatId);
        }
    }
}
