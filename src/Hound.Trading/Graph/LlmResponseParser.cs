using System.Text.RegularExpressions;

namespace Hound.Trading.Graph;

internal static partial class LlmResponseParser
{
    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.Compiled)]
    private static partial Regex ThinkBlockRegex();

    internal static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        // Strip <think>...</think> blocks (qwen3 chain-of-thought)
        trimmed = ThinkBlockRegex().Replace(trimmed, "").Trim();

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

        // Find the last balanced JSON object — LLM reasoning often contains
        // partial JSON fragments, so search backwards from the end.
        var json = ExtractLastBalancedObject(trimmed);
        if (json is not null)
            return json;

        return trimmed;
    }

    private static string? ExtractLastBalancedObject(string text)
    {
        // Walk backwards to find the last '}', then match it to its opening '{'
        for (var end = text.Length - 1; end >= 0; end--)
        {
            if (text[end] != '}')
                continue;

            var depth = 0;
            var inString = false;
            var escape = false;

            for (var i = end; i >= 0; i--)
            {
                var c = text[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                    continue;

                if (c == '}')
                    depth++;
                else if (c == '{')
                    depth--;

                if (depth == 0)
                    return text[i..(end + 1)];
            }

            // Unbalanced — try the next '}' further back
        }

        return null;
    }
}
