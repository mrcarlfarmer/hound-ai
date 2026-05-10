namespace Hound.Trading.Graph;

/// <summary>
/// Clears Ollama KV caches between long-running monitor loops
/// to free VRAM on resource-constrained hardware.
/// </summary>
public interface IResettableExecutor
{
    Task ResetAsync(CancellationToken cancellationToken);
}
