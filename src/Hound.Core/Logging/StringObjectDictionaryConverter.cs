using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hound.Core.Logging;

/// <summary>
/// Converter for <see cref="Dictionary{TKey, TValue}"/> of <c>string</c> →
/// <c>object</c> that materialises JSON values into native CLR primitives
/// (rather than leaving them as <see cref="JsonElement"/>). This avoids
/// "JsonElement is in invalid state" exceptions when the backing
/// <see cref="JsonDocument"/> — including RavenDB.Client 7.x's pooled session
/// buffers — is disposed before the dictionary is serialised again.
/// </summary>
public sealed class StringObjectDictionaryConverter : JsonConverter<Dictionary<string, object>>
{
    public override Dictionary<string, object> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for metadata dictionary.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var dict = new Dictionary<string, object>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = ConvertElement(property.Value);
            if (value is not null)
            {
                dict[property.Name] = value;
            }
        }
        return dict;
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, item) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, item, item?.GetType() ?? typeof(object), options);
        }
        writer.WriteEndObject();
    }

    private static object? ConvertElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l)
            ? l
            : element.TryGetDecimal(out var dec) ? dec : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray()
            .Select(ConvertElement)
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList(),
        JsonValueKind.Object => element.EnumerateObject()
            .Where(p => ConvertElement(p.Value) is not null)
            .ToDictionary(p => p.Name, p => ConvertElement(p.Value)!),
        _ => null,
    };
}
