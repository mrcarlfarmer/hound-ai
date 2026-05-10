namespace Hound.Trading.Graph;

internal static class LlmResponseParser
{
    internal static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        // Strip markdown code fences: ```json ... ``` or ``` ... ```
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];

            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3];

            trimmed = trimmed.Trim();
        }

        // Extract first JSON object if surrounded by other text
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return trimmed;
    }
}
