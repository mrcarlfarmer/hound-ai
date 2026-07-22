using System.Text.Json;
using System.Text.Json.Serialization;
using Hound.Grocery.Config;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Services;

/// <summary>
/// <see cref="IShoppingListParser"/> backed by the keyed <c>default</c>
/// <see cref="IChatClient"/> (gemma4:12b). It sends the persona system prompt
/// plus the untrusted user message and parses the model's JSON reply into
/// <see cref="ConciergeIntent"/>s. Any failure degrades gracefully to a single
/// <see cref="ConciergeIntentKind.Unknown"/> intent so a malformed or hostile
/// message never mutates the list.
/// </summary>
public class LlmShoppingListParser : IShoppingListParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IChatClient _chatClient;
    private readonly TelegramSettings _telegram;
    private readonly ILogger<LlmShoppingListParser>? _logger;

    public LlmShoppingListParser(
        IChatClient chatClient,
        IOptions<TelegramSettings> telegramOptions,
        ILoggerFactory? loggerFactory = null)
    {
        _chatClient = chatClient;
        _telegram = telegramOptions.Value;
        _logger = loggerFactory?.CreateLogger<LlmShoppingListParser>();
    }

    public async Task<ShoppingListParseResult> ParseAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return ShoppingListParseResult.Unknown;
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, PersonaPrompt.BuildParserSystemPrompt(_telegram)),
            new(ChatRole.User, message),
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            var intents = ParseIntents(response.Text ?? string.Empty);
            return intents.Count == 0 ? ShoppingListParseResult.Unknown : new ShoppingListParseResult(intents);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Shopping-list parse failed; treating message as unknown.");
            return ShoppingListParseResult.Unknown;
        }
    }

    internal static IReadOnlyList<ConciergeIntent> ParseIntents(string modelText)
    {
        var json = ExtractJson(modelText);
        if (json is null)
        {
            return [];
        }

        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (envelope?.Intents is null || envelope.Intents.Count == 0)
        {
            return [];
        }

        var result = new List<ConciergeIntent>();
        foreach (var dto in envelope.Intents)
        {
            var kind = MapKind(dto.Action);
            var item = string.IsNullOrWhiteSpace(dto.Item) ? null : dto.Item.Trim().ToLowerInvariant();
            var query = string.IsNullOrWhiteSpace(dto.Query) ? null : dto.Query.Trim();

            // Drop nonsensical mutation intents that lack a target item.
            if ((kind is ConciergeIntentKind.Add or ConciergeIntentKind.Remove or ConciergeIntentKind.SetQuantity)
                && item is null)
            {
                continue;
            }

            result.Add(new ConciergeIntent(kind, item, dto.Quantity, query));
        }

        return result;
    }

    private static ConciergeIntentKind MapKind(string? action) => action?.Trim().ToLowerInvariant() switch
    {
        "add" => ConciergeIntentKind.Add,
        "remove" => ConciergeIntentKind.Remove,
        "set_quantity" or "setquantity" or "set-quantity" => ConciergeIntentKind.SetQuantity,
        "query" => ConciergeIntentKind.Query,
        _ => ConciergeIntentKind.Unknown,
    };

    /// <summary>
    /// Extracts the first balanced JSON object from a model reply, tolerating
    /// surrounding prose or <c>```json</c> fences.
    /// </summary>
    internal static string? ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text.Substring(start, end - start + 1);
    }

    private sealed record Envelope(
        [property: JsonPropertyName("intents")] List<IntentDto>? Intents);

    private sealed record IntentDto(
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("item")] string? Item,
        [property: JsonPropertyName("quantity")] double? Quantity,
        [property: JsonPropertyName("query")] string? Query);
}
