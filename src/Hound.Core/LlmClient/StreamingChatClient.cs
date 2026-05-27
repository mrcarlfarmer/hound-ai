using System.Runtime.CompilerServices;
using System.Text;
using Hound.Core.Models;
using Microsoft.Extensions.AI;

namespace Hound.Core.LlmClient;

/// <summary>
/// <see cref="DelegatingChatClient"/> middleware that forwards every streamed
/// LLM update to the ambient <see cref="NodeStreamContext"/>. Non-streaming
/// <see cref="GetResponseAsync"/> calls are transparently converted to streaming
/// under the hood so reasoning is visible even when nodes use the simpler API.
/// </summary>
public sealed class StreamingChatClient : DelegatingChatClient
{
    // Coalesce small token deltas to keep network/UI chatter manageable.
    private const int FlushCharThreshold = 32;

    private readonly ChatOptions? _defaultOptions;

    public StreamingChatClient(IChatClient innerClient, ChatOptions? defaultOptions = null)
        : base(innerClient)
    {
        _defaultOptions = defaultOptions;
    }

    private ChatOptions? MergeOptions(ChatOptions? callOptions)
    {
        if (_defaultOptions is null) return callOptions;
        if (callOptions is null) return _defaultOptions;

        // Call-site options take precedence; fill in defaults for unset fields
        callOptions.Temperature ??= _defaultOptions.Temperature;
        callOptions.TopP ??= _defaultOptions.TopP;
        return callOptions;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = MergeOptions(options);
        var ctx = NodeStreamContext.Current;
        if (ctx is null)
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        // Stream under the hood so we can emit chunks even though the caller
        // is awaiting a single aggregated response.
        var updates = new List<ChatResponseUpdate>();
        var buffer = new StringBuilder();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            updates.Add(update);
            AppendAndMaybeFlush(buffer, update, ctx, force: false);
        }
        Flush(buffer, ctx);
        return updates.ToChatResponse();
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options = MergeOptions(options);
        var ctx = NodeStreamContext.Current;
        var buffer = ctx is null ? null : new StringBuilder();

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            if (ctx is not null && buffer is not null)
            {
                AppendAndMaybeFlush(buffer, update, ctx, force: false);
            }
            yield return update;
        }

        if (ctx is not null && buffer is not null)
        {
            Flush(buffer, ctx);
        }
    }

    private static void AppendAndMaybeFlush(StringBuilder buffer, ChatResponseUpdate update, NodeStreamContext ctx, bool force)
    {
        var text = update.Text;
        if (!string.IsNullOrEmpty(text))
        {
            buffer.Append(text);
        }
        if (force || buffer.Length >= FlushCharThreshold)
        {
            Flush(buffer, ctx);
        }
    }

    private static void Flush(StringBuilder buffer, NodeStreamContext ctx)
    {
        if (buffer.Length == 0) return;
        ctx.Publisher.Publish(new NodeStreamChunk
        {
            RunId = ctx.RunId,
            NodeId = ctx.NodeId,
            Text = buffer.ToString(),
            Timestamp = DateTime.UtcNow,
        });
        buffer.Clear();
    }
}
