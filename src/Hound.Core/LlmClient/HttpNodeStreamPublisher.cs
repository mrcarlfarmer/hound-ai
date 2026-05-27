using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Channels;
using Hound.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hound.Core.LlmClient;

/// <summary>
/// Buffered HTTP-based node stream publisher. Chunks are queued on an unbounded
/// channel and flushed by a background worker so that LLM token streaming never
/// blocks on network IO.
/// </summary>
public sealed class HttpNodeStreamPublisher : BackgroundService, INodeStreamPublisher
{
    private readonly Channel<NodeStreamChunk> _queue = Channel.CreateUnbounded<NodeStreamChunk>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<string, StringBuilder> _reasoning = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;
    private readonly ILogger<HttpNodeStreamPublisher>? _logger;

    public HttpNodeStreamPublisher(
        IHttpClientFactory httpClientFactory,
        string baseUrl,
        ILogger<HttpNodeStreamPublisher>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
    }

    public void Publish(NodeStreamChunk chunk)
    {
        var buffer = _reasoning.GetOrAdd(Key(chunk.RunId, chunk.NodeId), _ => new StringBuilder());
        lock (buffer)
        {
            buffer.Append(chunk.Text);
        }
        _queue.Writer.TryWrite(chunk);
    }

    public string? GetReasoning(string runId, string nodeId)
    {
        if (!_reasoning.TryGetValue(Key(runId, nodeId), out var buffer))
            return null;
        lock (buffer)
        {
            return buffer.Length == 0 ? null : buffer.ToString();
        }
    }

    private static string Key(string runId, string nodeId) => $"{runId}::{nodeId}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = _httpClientFactory.CreateClient();
        await foreach (var chunk in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var response = await client.PostAsJsonAsync(
                    $"{_baseUrl}/api/runs/events/node-stream", chunk, stoppingToken);
                // Ignore non-success — dashboard streaming is best-effort.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to publish node stream chunk");
            }
        }
    }
}
