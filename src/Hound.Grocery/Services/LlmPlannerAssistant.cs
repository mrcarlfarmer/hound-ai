using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Hound.Grocery.Services;

/// <summary>
/// <see cref="IPlannerAssistant"/> backed by the keyed <c>default</c>
/// <see cref="IChatClient"/> (gemma4:12b, thinking mode). For an item with no
/// learned preference it asks the model for a search term, a product hint and a
/// default weekly quantity, returning strict JSON.
/// <para>
/// Robust to cold start and model trouble: a blank item, a model failure or
/// unparseable output all degrade to a safe fallback (raw item as the search
/// term, no hint, quantity 1) so planning never crashes.
/// </para>
/// </summary>
public class LlmPlannerAssistant : IPlannerAssistant
{
    internal const string SystemPrompt =
        """
        You help plan a UK household's weekly Sainsbury's grocery shop. Given ONE
        loose item from a shopping list, propose how to find it.

        Respond with ONLY a single JSON object — no markdown, no prose, no fences:
        {"search_term":"<text>","product_hint":"<typical product name or null>","quantity":<number>}

        Rules:
        - "search_term": the best short phrase to type into the grocery search box.
        - "product_hint": a sensible typical product (brand/size) if you can name
          one, else null. Do NOT invent precise prices or product codes.
        - "quantity": a reasonable DEFAULT weekly quantity for a household (a small
          positive number; use 1 if unsure).
        - Output raw JSON only.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IChatClient _chatClient;
    private readonly ILogger<LlmPlannerAssistant>? _logger;

    public LlmPlannerAssistant(IChatClient chatClient, ILoggerFactory? loggerFactory = null)
    {
        _chatClient = chatClient;
        _logger = loggerFactory?.CreateLogger<LlmPlannerAssistant>();
    }

    public async Task<PlanSuggestion> SuggestAsync(string item, CancellationToken cancellationToken)
    {
        var fallback = Fallback(item);
        if (string.IsNullOrWhiteSpace(item))
        {
            return fallback;
        }

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, item.Trim()),
        };

        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            return ParseSuggestion(response.Text ?? string.Empty, item) ?? fallback;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Planner assistant failed for '{Item}'; using fallback suggestion.", item);
            return fallback;
        }
    }

    internal static PlanSuggestion Fallback(string item)
    {
        var term = string.IsNullOrWhiteSpace(item) ? string.Empty : item.Trim();
        return new PlanSuggestion(term, null, 1);
    }

    internal static PlanSuggestion? ParseSuggestion(string modelText, string item)
    {
        var json = ExtractJson(modelText);
        if (json is null)
        {
            return null;
        }

        SuggestionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<SuggestionDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto is null)
        {
            return null;
        }

        var searchTerm = string.IsNullOrWhiteSpace(dto.SearchTerm) ? item.Trim() : dto.SearchTerm.Trim();
        var hint = string.IsNullOrWhiteSpace(dto.ProductHint) || IsNullLiteral(dto.ProductHint)
            ? null
            : dto.ProductHint.Trim();
        var quantity = dto.Quantity is > 0 ? dto.Quantity.Value : 1;

        return new PlanSuggestion(searchTerm, hint, quantity);
    }

    private static bool IsNullLiteral(string value) =>
        string.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase);

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

    private sealed record SuggestionDto(
        [property: JsonPropertyName("search_term")] string? SearchTerm,
        [property: JsonPropertyName("product_hint")] string? ProductHint,
        [property: JsonPropertyName("quantity")] double? Quantity);
}
