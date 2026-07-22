using System.Text.Json;
using Hound.Core.MarketIntel;
using Hound.Trading.Services.News;
using Microsoft.Extensions.Logging;

namespace Hound.Trading.Services;

/// <summary>
/// StockTwits-backed sentiment service. Calls the public
/// <c>/api/2/streams/symbol/{symbol}.json</c> endpoint (no auth) and counts
/// the <c>entities.sentiment.basic</c> values across the most recent message
/// stream. Errors are swallowed and surfaced as <see cref="SentimentSnapshot.Empty"/>.
/// </summary>
public sealed class StockTwitsSentimentService : ISentimentService
{
    private const string Source = "StockTwits";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StockTwitsSentimentService>? _logger;

    public StockTwitsSentimentService(
        IHttpClientFactory httpClientFactory,
        ILogger<StockTwitsSentimentService>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SentimentSnapshot> GetSentimentAsync(
        string symbol,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol) || maxMessages <= 0)
            return SentimentSnapshot.Empty(symbol, Source);

        var url = $"https://api.stocktwits.com/api/2/streams/symbol/{Uri.EscapeDataString(symbol)}.json";
        using var client = _httpClientFactory.CreateClient(NewsHttpClients.RssClientName);

        try
        {
            var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return SentimentSnapshot.Empty(symbol, Source);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return Parse(stream, symbol, maxMessages);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "StockTwits sentiment lookup failed for {Symbol}; returning empty snapshot.", symbol);
            return SentimentSnapshot.Empty(symbol, Source);
        }
    }

    internal static SentimentSnapshot Parse(Stream json, string symbol, int maxMessages)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement, symbol, maxMessages);
        }
        catch (JsonException)
        {
            return SentimentSnapshot.Empty(symbol, Source);
        }
    }

    internal static SentimentSnapshot Parse(string json, string symbol, int maxMessages)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement, symbol, maxMessages);
        }
        catch (JsonException)
        {
            return SentimentSnapshot.Empty(symbol, Source);
        }
    }

    private static SentimentSnapshot Parse(JsonElement root, string symbol, int maxMessages)
    {
        if (!root.TryGetProperty("messages", out var messages) ||
            messages.ValueKind != JsonValueKind.Array)
        {
            return SentimentSnapshot.Empty(symbol, Source);
        }

        int bullish = 0, bearish = 0, neutral = 0;
        var recent = new List<string>(maxMessages);

        foreach (var msg in messages.EnumerateArray())
        {
            string? basic = null;
            if (msg.TryGetProperty("entities", out var entities) &&
                entities.ValueKind == JsonValueKind.Object &&
                entities.TryGetProperty("sentiment", out var sentiment) &&
                sentiment.ValueKind == JsonValueKind.Object &&
                sentiment.TryGetProperty("basic", out var basicEl) &&
                basicEl.ValueKind == JsonValueKind.String)
            {
                basic = basicEl.GetString();
            }

            if (string.Equals(basic, "Bullish", StringComparison.OrdinalIgnoreCase)) bullish++;
            else if (string.Equals(basic, "Bearish", StringComparison.OrdinalIgnoreCase)) bearish++;
            else neutral++;

            if (recent.Count < maxMessages &&
                msg.TryGetProperty("body", out var body) &&
                body.ValueKind == JsonValueKind.String)
            {
                var text = body.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    recent.Add(text.Trim());
            }
        }

        return new SentimentSnapshot(
            Symbol: symbol,
            Source: Source,
            Bullish: bullish,
            Bearish: bearish,
            Neutral: neutral,
            RecentMessages: recent,
            RetrievedAt: DateTimeOffset.UtcNow);
    }
}
