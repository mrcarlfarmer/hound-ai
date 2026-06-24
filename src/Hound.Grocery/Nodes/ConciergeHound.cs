using System.Globalization;
using System.Text;
using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Grocery.Config;
using Hound.Grocery.Graph;
using Hound.Grocery.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Nodes;

/// <summary>
/// Telegram front door + persona. Parses natural-language messages
/// (add/remove/set-quantity/query) and slash commands, maintains
/// <c>shopping-list.md</c> via <see cref="ShoppingListService"/>, and relays
/// brief, persona-flavoured confirmations.
/// <para>
/// <b>Phase 2:</b> continuous intake (spec §7.1) is implemented here behind
/// <see cref="IConciergeMessageHandler"/>; <see cref="TelegramIntakeService"/>
/// owns the long-poll loop and authorisation gate. The scheduled basket-build
/// role (<see cref="ExecuteAsync"/>) remains a reporting stub until later phases.
/// </para>
/// </summary>
public class ConciergeHound : INode, IConciergeMessageHandler
{
    public string NodeId => "concierge-hound";
    public string PackId => GroceryPack.PackId;

    private readonly IActivityLogger _activityLogger;
    private readonly IShoppingListParser _parser;
    private readonly ShoppingListService _shoppingList;
    private readonly TelegramSettings _telegram;
    private readonly ILogger<ConciergeHound>? _logger;

    public ConciergeHound(
        IActivityLogger activityLogger,
        IShoppingListParser parser,
        ShoppingListService shoppingList,
        IOptions<TelegramSettings> telegramOptions,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _parser = parser;
        _shoppingList = shoppingList;
        _telegram = telegramOptions.Value;
        _logger = loggerFactory?.CreateLogger<ConciergeHound>();
    }

    public async Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("ConciergeHound reporting role invoked for run {RunId}", state.RunId);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(ConciergeHound),
            Message = "ConciergeHound reached the reporting phase",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        return state with { CurrentNode = NodeId, Phase = GroceryPhase.Reporting };
    }

    public async Task<ConciergeReply> HandleMessageAsync(
        TelegramIncomingMessage message,
        CancellationToken cancellationToken)
    {
        var text = message.Text.Trim();
        if (text.Length == 0)
        {
            return new ConciergeReply("Send me items to add (e.g. \"add 2 milk\"), or /help.");
        }

        return text.StartsWith('/')
            ? await HandleCommandAsync(text, cancellationToken)
            : await HandleNaturalLanguageAsync(message, text, cancellationToken);
    }

    private async Task<ConciergeReply> HandleCommandAsync(string text, CancellationToken cancellationToken)
    {
        var command = text.Split(' ', 2)[0].ToLowerInvariant();

        switch (command)
        {
            case "/start":
            case "/help":
                return new ConciergeReply(HelpText());

            case "/list":
                var summary = await _shoppingList.RenderSummaryAsync(cancellationToken);
                return new ConciergeReply(summary);

            case "/build":
            case "/budget":
            case "/status":
            case "/approve":
            case "/trim":
                return new ConciergeReply(
                    $"{command} isn't available yet — that arrives in a later phase. For now I can manage the shopping list. Try /help.");

            default:
                return new ConciergeReply($"I don't recognise {command}. Try /help.");
        }
    }

    private async Task<ConciergeReply> HandleNaturalLanguageAsync(
        TelegramIncomingMessage message,
        string text,
        CancellationToken cancellationToken)
    {
        var parsed = await _parser.ParseAsync(text, cancellationToken);
        var actionable = parsed.Intents
            .Where(i => i.Kind != ConciergeIntentKind.Unknown)
            .ToList();

        if (actionable.Count == 0)
        {
            return new ConciergeReply(
                "Sorry, I didn't catch any grocery changes there. Try \"add 2 milk\", \"remove eggs\", or /help.");
        }

        var confirmations = new List<string>();
        var changed = false;
        var queryRequested = false;

        foreach (var intent in actionable)
        {
            switch (intent.Kind)
            {
                case ConciergeIntentKind.Add:
                    var added = await _shoppingList.AddOrUpdateAsync(
                        intent.ItemName!, intent.Quantity ?? 1, message.SenderName, DateTime.UtcNow.Date, cancellationToken);
                    confirmations.Add($"added {added.Name} (qty ~{ShoppingListService.FormatQuantity(added.Quantity)})");
                    changed = true;
                    break;

                case ConciergeIntentKind.SetQuantity:
                    var updated = await _shoppingList.SetQuantityAsync(
                        intent.ItemName!, intent.Quantity ?? 1, message.SenderName, DateTime.UtcNow.Date, cancellationToken);
                    confirmations.Add($"set {updated.Name} to qty ~{ShoppingListService.FormatQuantity(updated.Quantity)}");
                    changed = true;
                    break;

                case ConciergeIntentKind.Remove:
                    var removed = await _shoppingList.RemoveAsync(intent.ItemName!, cancellationToken);
                    confirmations.Add(removed
                        ? $"removed {intent.ItemName}"
                        : $"{intent.ItemName} wasn't on the list");
                    changed |= removed;
                    break;

                case ConciergeIntentKind.Query:
                    queryRequested = true;
                    break;
            }
        }

        if (changed)
        {
            await LogListUpdatedAsync(message, confirmations, cancellationToken);
        }

        var reply = BuildReply(confirmations);

        if (queryRequested)
        {
            var summary = await _shoppingList.RenderSummaryAsync(cancellationToken);
            reply = reply.Length == 0 ? summary : $"{reply}\n\n{summary}";
        }

        if (reply.Length == 0)
        {
            reply = "Done.";
        }

        return new ConciergeReply(reply, changed);
    }

    private async Task LogListUpdatedAsync(
        TelegramIncomingMessage message,
        IReadOnlyList<string> confirmations,
        CancellationToken cancellationToken)
    {
        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = NodeId,
            HoundName = nameof(ConciergeHound),
            Message = $"ListUpdated: {string.Join("; ", confirmations)}",
            Severity = ActivitySeverity.Info,
            Metadata = new Dictionary<string, object>
            {
                ["event"] = "ListUpdated",
                ["sender"] = message.SenderName,
                ["chatId"] = message.ChatId,
            },
        }, cancellationToken);
    }

    private static string BuildReply(IReadOnlyList<string> confirmations)
    {
        if (confirmations.Count == 0)
        {
            return string.Empty;
        }

        var sentence = string.Join(", ", confirmations);
        var capitalised = char.ToUpper(sentence[0], CultureInfo.InvariantCulture) + sentence[1..];
        return capitalised + ".";
    }

    private string HelpText()
    {
        var name = string.IsNullOrWhiteSpace(_telegram.PersonaName) ? "Sous" : _telegram.PersonaName.Trim();
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Hi, I'm {name} — your grocery list helper.");
        builder.AppendLine("Just tell me what you need:");
        builder.AppendLine("• \"add 2 milk\" — add an item");
        builder.AppendLine("• \"remove eggs\" — take something off");
        builder.AppendLine("• \"make it 3 bananas\" — change a quantity");
        builder.AppendLine("• \"what's on the list?\" — read it back");
        builder.AppendLine();
        builder.AppendLine("Commands: /list, /help");
        return builder.ToString().TrimEnd();
    }
}
